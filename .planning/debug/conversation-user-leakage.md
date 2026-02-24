---
status: diagnosed
trigger: "Conversations started on user 1 are visible/stored when logging in as user 2. Conversations leak between users."
created: 2026-02-24T00:00:00Z
updated: 2026-02-24T00:00:00Z
---

## Current Focus

hypothesis: ListConversations and SendMessage have no user scoping — the data model has no UserId column on ChatMessageEntity or SessionEntity, and the controller queries all rows globally
test: Read ConversationsController, ChatMessageEntity, SessionEntity, AppDbContext
expecting: Confirmed — no WHERE userId = currentUser filter anywhere in the chat pipeline
next_action: DONE — root cause confirmed

## Symptoms

expected: Each user should only see their own conversations. Logging in as a different user should show only that user's conversations.
actual: After starting a conversation as user 1, logging in as user 2 still shows/has access to user 1's conversation.
errors: None reported
reproduction: 1. Log in as user 1, start a conversation. 2. Log out. 3. Log in as user 2. 4. User 2 sees user 1's conversation.
started: Unknown — likely always present

## Eliminated

- hypothesis: Frontend caches conversations in memory/localStorage and doesn't clear on logout
  evidence: The API itself returns all conversations without user scoping; frontend state is irrelevant — even a fresh request returns leaked data
  timestamp: 2026-02-24T00:00:00Z

## Evidence

- timestamp: 2026-02-24T00:00:00Z
  checked: src/WebApi/Controllers/ConversationsController.cs — ListConversations()
  found: |
    The query is:
      dbContext.ChatMessages
        .Where(m => m.SessionId != null && m.MessageText != null)
        .GroupBy(m => m.SessionId!)
        .Select(...)
    There is NO .Where(m => m.UserId == currentUserId) filter.
    The entire ChatMessages table is queried globally.
  implication: Every authenticated user receives every conversation from every user.

- timestamp: 2026-02-24T00:00:00Z
  checked: src/WebApi/Controllers/ConversationsController.cs — GetConversation() and DeleteConversation()
  found: Both query by SessionId only, with no user ownership check. Any authenticated user can read or delete any conversation by guessing/knowing its ID.
  implication: Full horizontal privilege escalation on read and delete.

- timestamp: 2026-02-24T00:00:00Z
  checked: src/WebApi/Controllers/ConversationsController.cs — SendMessage()
  found: sessionManager.CreateNewSessionAsync() and LoadSessionAsync() have no user context passed. Sessions are created and loaded globally.
  implication: A user can load and continue another user's conversation if they know the session ID.

- timestamp: 2026-02-24T00:00:00Z
  checked: src/WebApi/Models/EfCore/Chat/ChatMessageEntity.cs and SessionEntity.cs
  found: |
    ChatMessageEntity fields: Key, SessionId, Timestamp, SerializedMessage, MessageText, EmittedTags
    SessionEntity fields: Id, SerializedState, CreatedAt, UpdatedAt
    NEITHER entity has a UserId column.
  implication: There is no data-layer ownership tracking at all. The schema itself does not associate sessions or messages with users.

- timestamp: 2026-02-24T00:00:00Z
  checked: src/WebApi/Data/AppDbContext.cs — modelBuilder config for ChatMessageEntity and SessionEntity
  found: No UserId foreign key, no index on UserId, no relationship to the User table defined for either chat entity.
  implication: Confirms the schema omission is not just a missing filter — the column does not exist and would require a migration to add.

- timestamp: 2026-02-24T00:00:00Z
  checked: src/WebApi/Controllers/ConversationsController.cs — [Authorize] attribute
  found: The controller class has no [Authorize] attribute. Only SendMessage, ListConversations, GetConversation, DeleteConversation are public endpoints — none have [Authorize] on them individually either. stream-test is explicitly [AllowAnonymous].
  implication: Unclear whether the route is globally protected via middleware/policy. If not, conversations may be accessible without any authentication at all.

## Resolution

root_cause: |
  The ChatMessageEntity and SessionEntity database models have NO UserId column.
  Neither the Sessions nor ChatMessages tables track which user owns a session.

  As a result, ConversationsController.ListConversations() returns ALL conversations
  from ALL users with no filtering. GetConversation() and DeleteConversation() similarly
  operate on any session by ID with no ownership check. SendMessage() creates and loads
  sessions with no user association.

  This is a multi-layer failure:
    1. Schema layer — UserId is not a column on ChatMessageEntity or SessionEntity
    2. Service layer — SessionManager has no concept of user context
    3. Controller layer — no user-scoped WHERE clauses anywhere in the chat pipeline

fix: |
  Requires three coordinated changes:

  1. SCHEMA — Add UserId (Guid, FK to Users) to both ChatMessageEntity and SessionEntity.
     Create an EF Core migration. Add indexes on UserId for both tables.

  2. SERVICE/CONTROLLER — On session creation (SendMessage with no ConversationId),
     resolve the authenticated user's ID from HttpContext and write it to the new UserId column.
     In SessionManager.CreateNewSessionAsync(), accept and persist a userId parameter.

  3. QUERY FILTERING — In ListConversations(), add:
       .Where(m => m.UserId == currentUserId)
     In GetConversation() and DeleteConversation(), add an ownership assertion:
       if (entity.UserId != currentUserId) return Forbid();
     In LoadSessionAsync(), verify the session's UserId matches the requesting user.

  Additionally, confirm [Authorize] is applied globally or explicitly to all
  ConversationsController endpoints (stream-test excluded).

verification: Not yet applied — diagnosis only per goal: find_root_cause_only
files_changed: []
