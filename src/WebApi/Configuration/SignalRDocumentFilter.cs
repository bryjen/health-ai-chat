using Swashbuckle.AspNetCore.SwaggerGen;

namespace WebApi.Configuration;

/// <summary>
/// Custom Swagger document filter to add SignalR hub documentation as separate operations
/// </summary>
public class SignalRDocumentFilter : IDocumentFilter
{
    public void Apply(Microsoft.OpenApi.OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Add SignalR tags with descriptions
        swaggerDoc.Tags ??= new HashSet<Microsoft.OpenApi.OpenApiTag>();

        swaggerDoc.Tags.Add(new Microsoft.OpenApi.OpenApiTag
        {
            Name = "SignalR - Connection",
            Description = GetConnectionDocumentation()
        });

        swaggerDoc.Tags.Add(new Microsoft.OpenApi.OpenApiTag
        {
            Name = "SignalR - Requests",
            Description = GetRequestsDocumentation()
        });

        swaggerDoc.Tags.Add(new Microsoft.OpenApi.OpenApiTag
        {
            Name = "SignalR - Responses",
            Description = GetResponsesDocumentation()
        });

        // Create fake endpoints exactly like controllers do
        // Access the underlying Models document through reflection
        try
        {
            var documentType = swaggerDoc.GetType();
            var pathsProperty = documentType.GetProperty("Paths");
            if (pathsProperty != null)
            {
                var paths = pathsProperty.GetValue(swaggerDoc);
                if (paths != null)
                {
                    var pathsType = paths.GetType();
                    var addMethod = pathsType.GetMethod("Add");
                    
                    if (addMethod != null)
                    {
                        // Create paths exactly like controllers - with GET operations
                        AddPath(paths, addMethod, "/signalr/connection", "SignalR - Connection", "Establish WebSocket Connection", GetConnectionDocumentation());
                        AddPath(paths, addMethod, "/signalr/requests", "SignalR - Requests", "SendMessage Method", GetRequestsDocumentation());
                        AddPath(paths, addMethod, "/signalr/responses", "SignalR - Responses", "Response Structures and Events", GetResponsesDocumentation());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - tags with descriptions will still be added
            System.Diagnostics.Debug.WriteLine($"Failed to create SignalR paths: {ex.Message}");
        }
    }

    private static void AddPath(object paths, System.Reflection.MethodInfo addMethod, string path, string tag, string summary, string description)
    {
        try
        {
            // Find Microsoft.OpenApi.Models types
            var pathItemType = FindType("Microsoft.OpenApi.Models.OpenApiPathItem");
            var operationType = FindType("Microsoft.OpenApi.Models.OpenApiOperation");
            var operationTypeEnum = FindType("Microsoft.OpenApi.Models.OperationType");
            var tagType = FindType("Microsoft.OpenApi.Models.OpenApiTag");
            var responsesType = FindType("Microsoft.OpenApi.Models.OpenApiResponses");
            var responseType = FindType("Microsoft.OpenApi.Models.OpenApiResponse");

            if (pathItemType == null || operationType == null || operationTypeEnum == null)
                return;

            // Create OpenApiPathItem
            var pathItem = System.Activator.CreateInstance(pathItemType);

            // Create OpenApiOperation
            var operation = System.Activator.CreateInstance(operationType);

            // Set Tags
            if (tagType != null)
            {
                var tagsListType = typeof(System.Collections.Generic.List<>).MakeGenericType(tagType);
                var tagsList = System.Activator.CreateInstance(tagsListType);
                var tagInstance = System.Activator.CreateInstance(tagType);
                tagType.GetProperty("Name")?.SetValue(tagInstance, tag);
                tagsListType.GetMethod("Add")?.Invoke(tagsList, new[] { tagInstance });
                operationType.GetProperty("Tags")?.SetValue(operation, tagsList);
            }

            // Set Summary
            operationType.GetProperty("Summary")?.SetValue(operation, summary);

            // Set Description
            operationType.GetProperty("Description")?.SetValue(operation, description);

            // Set Responses
            if (responsesType != null && responseType != null)
            {
                var responses = System.Activator.CreateInstance(responsesType);
                var response = System.Activator.CreateInstance(responseType);
                responseType.GetProperty("Description")?.SetValue(response, "Success");
                responsesType.GetMethod("Add")?.Invoke(responses, new object[] { "200", response });
                operationType.GetProperty("Responses")?.SetValue(operation, responses);
            }

            // Set Operations dictionary
            var operationsDictType = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(operationTypeEnum, operationType);
            var operationsDict = System.Activator.CreateInstance(operationsDictType);
            var getEnumValue = operationTypeEnum.GetField("Get")?.GetValue(null);
            if (getEnumValue != null)
            {
                operationsDictType.GetMethod("Add")?.Invoke(operationsDict, new[] { getEnumValue, operation });
                pathItemType.GetProperty("Operations")?.SetValue(pathItem, operationsDict);
            }

            // Add path to document - use IOpenApiPathItem interface
            var pathItemInterface = typeof(Microsoft.OpenApi.IOpenApiPathItem);
            if (pathItemInterface.IsAssignableFrom(pathItemType))
            {
                addMethod.Invoke(paths, new[] { path, pathItem });
            }
            else
            {
                // Try to cast or find the interface
                var interfaceType = FindType("Microsoft.OpenApi.IOpenApiPathItem");
                if (interfaceType != null && interfaceType.IsAssignableFrom(pathItemType))
                {
                    addMethod.Invoke(paths, new[] { path, pathItem });
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw
            System.Diagnostics.Debug.WriteLine($"Failed to add path {path}: {ex.Message}");
        }
    }

    private static Type? FindType(string fullTypeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullTypeName);
                if (type != null)
                    return type;
            }
            catch
            {
                // Continue searching
            }
        }
        return null;
    }

    private static string GetConnectionDocumentation()
    {
        return """
            Establishes a WebSocket connection to the Chat Hub for real-time bidirectional communication. Authentication is handled via JWT Bearer token.


            **Endpoint:** `/hubs/chat` (WebSocket)

            The Chat Hub provides real-time bidirectional communication for health chat functionality. It enables AI-powered health conversations with real-time status updates, symptom tracking, and assessment generation.


            #### C# Blazor WASM Example

            ```csharp
            using Microsoft.AspNetCore.SignalR.Client;

            // Inject ITokenProvider (or use your token management)
            var hubConnection = new HubConnectionBuilder()
                .WithUrl("/hubs/chat", options =>
                {
                    options.AccessTokenProvider = async () => 
                        await tokenProvider.GetTokenAsync();
                })
                .WithAutomaticReconnect(new[] 
                { 
                    TimeSpan.Zero, 
                    TimeSpan.FromSeconds(2), 
                    TimeSpan.FromSeconds(10), 
                    TimeSpan.FromSeconds(30) 
                })
                .Build();

            // Register event handlers before starting
            hubConnection.On<string>("StatusUpdate", async (statusJson) =>
            {
                var status = JsonSerializer.Deserialize<StatusInformation>(statusJson);
                // Handle status update
            });

            await hubConnection.StartAsync();
            ```


            #### JavaScript Example

            ```javascript
            const connection = new signalR.HubConnectionBuilder()
              .withUrl('/hubs/chat', {
                accessTokenFactory: async () => {
                  // Get your JWT token from storage or auth service
                  return localStorage.getItem('accessToken');
                }
              })
              .withAutomaticReconnect([0, 2000, 10000, 30000])
              .build();

            // Register event handlers before starting
            connection.on('StatusUpdate', (statusJson) => {
              const status = JSON.parse(statusJson);
              console.log('Status update:', status);
            });

            await connection.start();
            ```
            """;
    }

    private static string GetRequestsDocumentation()
    {
        return """
            Sends a health-related message to the AI assistant and processes it through the health chat orchestrator. The system automatically tracks symptoms, generates assessments, and provides medical guidance.


            **Method:** `SendMessage`


            **Parameters:**
            - `message` (string, required): The health-related message to send
            - `conversationId` (Guid?, optional): Optional conversation ID to continue an existing conversation. Omit or pass `null` to start a new conversation.


            #### C# Blazor WASM Example

            ```csharp
            try
            {
                var response = await hubConnection.InvokeAsync<HealthChatResponse>(
                    "SendMessage", 
                    "I've been experiencing headaches for the past 3 days", 
                    conversationId: null);

                Console.WriteLine($"Response: {response.Message}");
                Console.WriteLine($"Conversation ID: {response.ConversationId}");
                
                // Process entity changes
                foreach (var symptomChange in response.SymptomChanges)
                {
                    Console.WriteLine($"Symptom {symptomChange.Action}: {symptomChange.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            ```


            #### JavaScript Example

            ```javascript
            try
            {
              const response = await connection.invoke(
                'SendMessage', 
                'I\'ve been experiencing headaches for the past 3 days', 
                null
              );

              console.log('Response:', response.message);
              console.log('Conversation ID:', response.conversationId);
              
              // Process entity changes
              response.symptomChanges?.forEach(change => {
                console.log(`Symptom ${change.action}: ${change.name}`);
              });
            }
            catch (err) {
              console.error('Error:', err);
            }
            ```


            **Error Handling:**

            SignalR hub methods throw `HubException` on errors:

            **C# Example:**
            ```csharp
            try
            {
                var response = await hubConnection.InvokeAsync<HealthChatResponse>("SendMessage", message, null);
            }
            catch (HubException ex)
            {
                Console.WriteLine($"Hub error: {ex.Message}");
            }
            ```

            **JavaScript Example:**
            ```javascript
            try {
              const response = await connection.invoke('SendMessage', message, null);
            } catch (err) {
              if (err.errorType === 'HubException') {
                console.error('Hub error:', err.message);
              }
            }
            ```
            """;
    }

    private static string GetResponsesDocumentation()
    {
        return """
            Documentation for response structures and real-time events from the Chat Hub.


            ### SendMessage Response

            The `SendMessage` method returns a `HealthChatResponse` containing the AI response message, conversation ID, and tracked entity changes.


            **Response Structure:**

            ```json
            {
              "message": "Based on your symptoms, I recommend...",
              "conversationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
              "symptomChanges": [
                {
                  "id": "123",
                  "action": "added",
                  "name": "Headache",
                  "confidence": null
                }
              ],
              "appointmentChanges": [
                {
                  "id": "456",
                  "action": "created",
                  "name": null,
                  "confidence": null
                }
              ],
              "assessmentChanges": [
                {
                  "id": "789",
                  "action": "created",
                  "name": "Tension headache",
                  "confidence": 0.85
                }
              ]
            }
            ```


            ### StatusUpdate Event

            The server sends real-time status updates during message processing. These updates provide feedback about the AI's processing state, such as when assessments are being generated or analyzed.


            **Event Name:** `StatusUpdate`

            **Payload:** JSON string containing status information


            #### C# Blazor WASM Example

            ```csharp
            hubConnection.On<string>("StatusUpdate", async (statusJson) =>
            {
                var status = JsonSerializer.Deserialize<JsonElement>(statusJson);
                var statusType = status.GetProperty("type").GetString();

                switch (statusType)
                {
                    case "assessment-generating":
                        // Show "Generating assessment..." indicator
                        break;
                    case "assessment-analyzing":
                        // Show "Analyzing assessment..." indicator
                        break;
                    case "assessment-created":
                        var assessmentId = status.GetProperty("assessmentId").GetInt32();
                        var hypothesis = status.GetProperty("hypothesis").GetString();
                        var confidence = status.GetProperty("confidence").GetDecimal();
                        // Handle assessment created
                        break;
                }
            });
            ```


            #### JavaScript Example

            ```javascript
            connection.on('StatusUpdate', (statusJson) => {
              const status = JSON.parse(statusJson);
              
              switch (status.type) {
                case 'assessment-generating':
                  // Show "Generating assessment..." indicator
                  console.log('Generating assessment...');
                  break;
                  
                case 'assessment-analyzing':
                  // Show "Analyzing assessment..." indicator
                  console.log('Analyzing assessment...');
                  break;
                  
                case 'assessment-created':
                  const { assessmentId, hypothesis, confidence } = status;
                  console.log(`Assessment created: ${hypothesis} (${confidence})`);
                  break;
                  
                default:
                  console.log('Status update:', status);
              }
            });
            ```


            **Status Update Types:**

            Status updates are JSON objects with a `type` field and type-specific properties:


            **Assessment Generating:**
            ```json
            {
              "type": "assessment-generating",
              "message": "Generating assessment...",
              "timestamp": "2026-02-02T10:30:00Z"
            }
            ```


            **Assessment Analyzing:**
            ```json
            {
              "type": "assessment-analyzing",
              "message": "Analyzing assessment...",
              "timestamp": "2026-02-02T10:30:05Z"
            }
            ```


            **Assessment Created:**
            ```json
            {
              "type": "assessment-created",
              "assessmentId": 42,
              "hypothesis": "Tension headache",
              "confidence": 0.85,
              "timestamp": "2026-02-02T10:30:10Z"
            }
            ```
            """;
    }
}
