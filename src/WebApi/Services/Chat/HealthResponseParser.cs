using System.Text.Json;
using System.Text.RegularExpressions;
using Web.Common.DTOs.Health;

namespace WebApi.Services.Chat;

/// <summary>
/// Parses health chat AI responses, handling various JSON formats and fallback strategies.
/// </summary>
public class HealthResponseParser(ILogger<HealthResponseParser> logger)
{
    public HealthAssistantResponse Parse(string responseText)
    {
        try
        {
            // Try to extract JSON from response (might be wrapped in markdown code blocks)
            var jsonText = ExtractJsonFromResponse(responseText);
            
            // Parse JSON once and check if it has a "message" field
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            
            // Check for message field (case-insensitive)
            JsonElement? messageElement = null;
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, "message", StringComparison.OrdinalIgnoreCase))
                {
                    messageElement = prop.Value;
                    break;
                }
            }
            
            if (messageElement.HasValue)
            {
                // Try to deserialize the full response
                try
                {
                    var healthResponse = JsonSerializer.Deserialize<HealthAssistantResponse>(jsonText, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (healthResponse != null && !string.IsNullOrWhiteSpace(healthResponse.Message))
                    {
                        // Successfully parsed JSON - preserve status updates if they exist
                        healthResponse.StatusUpdatesSent = healthResponse.StatusUpdatesSent ?? new List<object>();
                        return healthResponse;
                    }
                }
                catch (JsonException)
                {
                    // Deserialization failed (e.g., other required fields missing), extract message manually
                    var messageText = messageElement.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(messageText))
                    {
                        return new HealthAssistantResponse
                        {
                            Message = messageText,
                            Appointment = null,
                            SymptomChanges = null,
                            StatusUpdatesSent = new List<object>()
                        };
                    }
                }
            }
        }
        catch (JsonException)
        {
            // JSON parsing failed, will fall through to fallback logic below
        }
        catch
        {
            // Other parsing errors, will fall through to fallback logic below
        }

        // If JSON parsing failed or message field missing, try to extract message text
        try
        {
            var jsonText = ExtractJsonFromResponse(responseText);
            using var doc = JsonDocument.Parse(jsonText);

            // Try case-insensitive property lookup
            JsonElement? messageElement = null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "message", StringComparison.OrdinalIgnoreCase))
                {
                    messageElement = prop.Value;
                    break;
                }
            }

            if (messageElement.HasValue)
            {
                var messageText = messageElement.Value.GetString();
                if (!string.IsNullOrWhiteSpace(messageText))
                {
                    return new HealthAssistantResponse
                    {
                        Message = messageText,
                        Appointment = null,
                        SymptomChanges = null,
                        StatusUpdatesSent = new List<object>()
                    };
                }
            }
        }
        catch
        {
            // If that fails, continue to fallback
        }

        // Final fallback: if response contains JSON-like structure, try to extract message field
        // Otherwise return the response as-is but clean it up
        var cleanedResponse = responseText.Trim();

        // Check if this is valid JSON but missing message field - this should not happen if validation worked
        try
        {
            var jsonText = ExtractJsonFromResponse(responseText);
            if (IsValidJson(jsonText))
            {
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;
                
                // Check if it's valid JSON but missing message field
                bool hasMessage = false;
                foreach (var prop in root.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "message", StringComparison.OrdinalIgnoreCase))
                    {
                        hasMessage = true;
                        break;
                    }
                }
                
                if (!hasMessage)
                {
                    logger.LogError("Response is valid JSON but missing required 'message' field. Response: {Response}", 
                        responseText.Substring(0, Math.Min(500, responseText.Length)));
                    // Return error message instead of raw JSON
                    return new HealthAssistantResponse
                    {
                        Message = "I apologize, but I encountered an error formatting my response. Please try again.",
                        Appointment = null,
                        SymptomChanges = null,
                        StatusUpdatesSent = new List<object>()
                    };
                }
            }
        }
        catch
        {
            // Not JSON, continue to regex fallback
        }

        // Remove any trailing JSON if message text appears before it
        var messageMatch = Regex.Match(
            cleanedResponse,
            @"""message""\s*:\s*""([^""]+)""",
            RegexOptions.IgnoreCase);

        if (messageMatch.Success && messageMatch.Groups.Count > 1)
        {
            var extractedMessage = messageMatch.Groups[1].Value;
            // Unescape JSON string
            extractedMessage = extractedMessage.Replace("\\n", "\n").Replace("\\\"", "\"");
            return new HealthAssistantResponse
            {
                Message = extractedMessage,
                Appointment = null,
                SymptomChanges = null
            };
        }

        // Last resort: return cleaned response (but log warning)
        logger.LogWarning("Could not parse JSON response, returning raw text. Response length: {Length}",
            responseText.Length);
        return new HealthAssistantResponse
        {
            Message = cleanedResponse,
            Appointment = null,
            SymptomChanges = null,
            StatusUpdatesSent = new List<object>()
        };
    }

    private static string ExtractJsonFromResponse(string response)
    {
        // Remove markdown code blocks if present
        var json = response.Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            json = json.Substring(7);
        }

        if (json.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            json = json.Substring(3);
        }

        if (json.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            json = json.Substring(0, json.Length - 3);
        }

        json = json.Trim();

        // Try to find JSON object boundaries if JSON is mixed with text
        // Look for first { and last } to extract just the JSON object
        var firstBrace = json.IndexOf('{');
        var lastBrace = json.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            // Extract just the JSON object part
            var jsonObject = json.Substring(firstBrace, lastBrace - firstBrace + 1);

            // Verify it's valid JSON by trying to parse it
            try
            {
                using var doc = JsonDocument.Parse(jsonObject);
                return jsonObject;
            }
            catch
            {
                // If extraction fails, return original
            }
        }

        return json;
    }

    private static bool IsValidJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }
}
