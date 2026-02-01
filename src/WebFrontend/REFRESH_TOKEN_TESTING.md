# Refresh Token Testing Guide

This document describes how to manually test the automatic token refresh functionality.

## Prerequisites

1. Backend API running on `https://localhost:7265/`
2. Frontend running on `https://localhost:5000/` (or configured port)
3. Browser developer tools open (F12) to monitor network requests

## Test Scenarios

### Test 1: Successful Automatic Refresh

**Objective**: Verify that expired access tokens are automatically refreshed and requests succeed.

**Steps**:
1. Log in to the application with valid credentials
2. Open browser DevTools → Network tab
3. Make note of the access token expiration time (check JWT payload or wait ~2 hours)
4. **Option A (Quick Test)**: Temporarily modify `JwtSettings.AccessTokenExpirationMinutes` in `appsettings.json` to `1` (1 minute) and restart the API
5. Wait for the access token to expire (or use the modified 1-minute expiry)
6. Perform any authenticated action (e.g., navigate to a protected page, send a chat message)
7. **Expected Result**:
   - Network tab shows a `401 Unauthorized` response for the original request
   - Immediately followed by a `POST /api/v1/auth/refresh` request
   - The refresh request succeeds with `200 OK`
   - The original request is retried and succeeds with `200 OK`
   - User remains logged in and can continue using the app

**Verification**:
- Check Network tab: Should see refresh call between failed and successful retry
- Check Application/Storage → Local Storage: Should see updated `authToken` and `refreshToken` values
- User should not see any error messages or be logged out

---

### Test 2: Multiple Concurrent Requests with Expired Token

**Objective**: Verify that multiple concurrent requests with expired tokens trigger only one refresh attempt.

**Steps**:
1. Log in to the application
2. Ensure access token is expired (wait or use modified expiry)
3. Open browser DevTools → Network tab
4. Quickly trigger multiple authenticated actions simultaneously:
   - Open multiple tabs/windows
   - Click multiple protected links
   - Send multiple API requests
5. **Expected Result**:
   - Only **one** `POST /api/v1/auth/refresh` request appears in the network tab
   - All original requests eventually succeed after the single refresh completes
   - No race conditions or multiple refresh attempts

**Verification**:
- Network tab: Count refresh requests (should be exactly 1)
- All requests should eventually succeed

---

### Test 3: Refresh Token Expired/Invalid

**Objective**: Verify that when refresh token is invalid or expired, user is logged out gracefully.

**Steps**:
1. Log in to the application
2. Open browser DevTools → Application/Storage → Local Storage
3. Manually modify the `refreshToken` value to an invalid string (e.g., "invalid_token")
4. Ensure access token is expired (wait or use modified expiry)
5. Perform an authenticated action
6. **Expected Result**:
   - Network tab shows `401 Unauthorized` for the original request
   - Refresh attempt is made (`POST /api/v1/auth/refresh`)
   - Refresh fails with `400 Bad Request` or `401 Unauthorized`
   - Tokens are cleared from Local Storage
   - User is redirected to `/auth` login page
   - Authentication state updates to unauthenticated

**Verification**:
- Network tab: Refresh request fails
- Local Storage: `authToken` and `refreshToken` are removed
- Browser navigates to `/auth` page
- User must log in again

---

### Test 4: Refresh Endpoint Protection

**Objective**: Verify that refresh endpoint requests don't trigger infinite refresh loops.

**Steps**:
1. Log in to the application
2. Open browser DevTools → Network tab
3. Manually call the refresh endpoint with an invalid refresh token:
   ```javascript
   fetch('https://localhost:7265/api/v1/auth/refresh', {
     method: 'POST',
     headers: { 'Content-Type': 'application/json' },
     body: JSON.stringify({ refreshToken: 'invalid' })
   })
   ```
4. **Expected Result**:
   - Refresh request fails (as expected)
   - No additional refresh attempts are made
   - No infinite loop occurs

**Verification**:
- Network tab: Only one refresh request (the manual one)
- No repeated refresh attempts

---

### Test 5: Network Failure During Refresh

**Objective**: Verify graceful handling when network fails during refresh attempt.

**Steps**:
1. Log in to the application
2. Open browser DevTools → Network tab
3. Set network throttling to "Offline" or disable network
4. Ensure access token is expired
5. Perform an authenticated action
6. **Expected Result**:
   - Original request fails (network unavailable)
   - Refresh attempt fails (network unavailable)
   - Tokens remain in Local Storage (not cleared on network error)
   - User sees network error, not authentication error

**Verification**:
- Network tab: Shows failed requests due to network
- Local Storage: Tokens still present
- Error message indicates network issue, not auth issue

---

### Test 6: Token Rotation Verification

**Objective**: Verify that refresh tokens are rotated (old token invalidated, new token issued).

**Steps**:
1. Log in to the application
2. Open browser DevTools → Application/Storage → Local Storage
3. Note the current `refreshToken` value
4. Ensure access token is expired
5. Perform an authenticated action (triggers refresh)
6. Check the new `refreshToken` value
7. Attempt to use the **old** refresh token value manually:
   ```javascript
   fetch('https://localhost:7265/api/v1/auth/refresh', {
     method: 'POST',
     headers: { 'Content-Type': 'application/json' },
     body: JSON.stringify({ refreshToken: '<old_refresh_token_value>' })
   })
   ```
8. **Expected Result**:
   - New `refreshToken` is different from the old one
   - Old refresh token request fails with `400 Bad Request`
   - Only the new refresh token works

**Verification**:
- Local Storage: `refreshToken` value changed after refresh
- Old token: Returns error when used
- New token: Works correctly

---

## Quick Test Setup (Temporary)

To speed up testing, temporarily modify `src/WebApi/appsettings.json`:

```json
{
  "Jwt": {
    "AccessTokenExpirationMinutes": 1,  // Changed from 120 to 1 minute
    "RefreshTokenExpirationDays": 30    // Keep this as-is
  }
}
```

**Important**: Remember to revert this change after testing!

---

## Troubleshooting

### Refresh Not Triggering
- Check that `TokenRefreshHttpMessageHandler` is registered in `Program.cs`
- Verify HTTP clients use the handler chain: `TokenRefreshHttpMessageHandler` → `TokenProviderHttpMessageHandler`
- Check browser console for errors

### Infinite Refresh Loop
- Verify `IsRefreshEndpoint()` correctly identifies refresh endpoint URLs
- Check that refresh endpoint doesn't use `TokenRefreshHttpMessageHandler`

### Tokens Not Updating
- Check Local Storage permissions in browser
- Verify `LocalStorageTokenProvider` methods are being called
- Check browser console for JavaScript errors

### User Not Redirected on Refresh Failure
- Verify `AuthStateProvider.NotifyUserChanged()` is called
- Check that `RedirectToLogin` component is in the app routing
- Verify authentication state is updating correctly
