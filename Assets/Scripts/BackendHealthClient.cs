using System;
using System.Collections;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class BackendHealthClient : MonoBehaviour
{
    [SerializeField] private string backendBaseUrl = "http://localhost:8000";
    [SerializeField] private string backendWebSocketUrl = "ws://localhost:8000/ws/agent";
    [SerializeField] private bool requestOnStart = true;
    [SerializeField] private bool checkWebSocketOnStart = false;
    [TextArea]
    [SerializeField] private string openAiHealthMessage = "Hello";

    private void Start()
    {
        if (requestOnStart)
        {
            CheckHealth();
            CheckOpenAiHealth();

            if (checkWebSocketOnStart)
            {
                CheckWebSocketHealth();
            }
        }
    }

    public void CheckHealth()
    {
        StartCoroutine(GetHealth());
    }

    public void CheckOpenAiHealth()
    {
        StartCoroutine(GetOpenAiHealth(openAiHealthMessage));
    }

    public void CheckOpenAiHealth(string message)
    {
        StartCoroutine(GetOpenAiHealth(message));
    }

    public async void CheckWebSocketHealth()
    {
        await CheckWebSocketConnection();
    }

    private IEnumerator GetHealth()
    {
        string url = $"{backendBaseUrl.TrimEnd('/')}/health";

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Health check failed: {request.responseCode} {request.error}");
            yield break;
        }

        string json = request.downloadHandler.text;
        HealthResponse response = JsonUtility.FromJson<HealthResponse>(json);

        Debug.Log(
            $"Health check OK: status={response.status}, service={response.service}, version={response.version}"
        );
    }

    private IEnumerator GetOpenAiHealth(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Debug.LogError("OpenAI health check failed: message is required.");
            yield break;
        }

        string encodedMessage = UnityWebRequest.EscapeURL(message);
        string url = $"{backendBaseUrl.TrimEnd('/')}/health/openai?message={encodedMessage}";

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"OpenAI health check failed: {request.responseCode} {request.error}");
            yield break;
        }

        string json = request.downloadHandler.text;
        OpenAiHealthResponse response = JsonUtility.FromJson<OpenAiHealthResponse>(json);

        Debug.Log(
            $"OpenAI health check OK: status={response.status}, input={response.input}, response={response.GetResponseText()}"
        );
    }

    private async Task CheckWebSocketConnection()
    {
        ClientWebSocket socket = new ClientWebSocket();
        CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await socket.ConnectAsync(new Uri(backendWebSocketUrl), cancellation.Token);
            Debug.Log($"WebSocket health check OK: url={backendWebSocketUrl}, state={socket.State}");
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Health check complete", CancellationToken.None);
        }
        catch (Exception exc)
        {
            Debug.LogError($"WebSocket health check failed: {exc.Message}");
        }
        finally
        {
            socket.Dispose();
            cancellation.Dispose();
        }
    }

    [Serializable]
    private class HealthResponse
    {
        public string status;
        public string service;
        public string version;
    }

    [Serializable]
    private class OpenAiHealthResponse
    {
        public string status;
        public string input;
        public string response;
        public OpenAiCommandResponse command;

        public string GetResponseText()
        {
            if (command != null)
            {
                return $"action={command.action}, destination={command.destination}, item={command.item}, message={command.message}";
            }

            return response ?? string.Empty;
        }
    }

    [Serializable]
    private class OpenAiCommandResponse
    {
        public string action;
        public string destination;
        public string item;
        public string message;
    }
}
