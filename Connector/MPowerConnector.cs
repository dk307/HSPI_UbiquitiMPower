using Hspi.Connector.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocket4Net;

namespace Hspi.Connector
{
    using Hspi.Exceptions;
    using static System.FormattableString;

    // Based on https://github.com/lnaundorf/pimatic-mpower/blob/master/mpower.coffee
    internal class MPowerConnector : IDisposable
    {
        public event EventHandler<IList<SensorData>> PortsChanged;

        public bool Connected
        {
            get
            {
                return webSocket != null ?
                    !(webSocket.State == WebSocketState.Closed || webSocket.State == WebSocketState.None) : false;
            }
        }

        public IPAddress DeviceIP { get; }
        public long TotalErrors => Interlocked.CompareExchange(ref totalErrors, 0, 0);

        public TimeSpan TimeSinceLastUpdate
        {
            get
            {
                lock (lastMessageLock) { return lastMessage.Elapsed; }
            }
        }

        public CancellationToken ConnectionClosed => cancelationTokenSourceForCompleted.Token;

        public MPowerConnector(IPAddress deviceIP, ILogger logger)
        {
            this.logger = logger;
            DeviceIP = deviceIP;
            sessionId = GenerateSessionId();
        }

        public async Task Connect(string userName, string password, CancellationToken token)
        {
            if (ConnectionClosed.IsCancellationRequested || webSocket != null)
            {
                throw new Exception("Connection is already closed/opened");
            }

            logger.LogDebug(Invariant($"Logging to {DeviceIP} with {sessionId}"));

            var postUrl = new Uri($"http://{DeviceIP}/login.cgi");
            string formDataString = $"username={WebUtility.UrlEncode(userName)}&password={WebUtility.UrlEncode(password)}";
            HttpWebRequest request = CreateFormWebRequest(postUrl, formDataString, "POST");

            string response = await ProcessRequest(request, token).ConfigureAwait(false);

            if (response.Contains("Invalid credentials"))
            {
                throw new Exception("Invalid Credentails.");
            }
            else if (response.Length > 0)
            {
                string errorMessage = Invariant($"Login Failed with {response}");
                throw new Exception(errorMessage);
            }

            logger.LogInfo(Invariant($"Logged to {DeviceIP} with {sessionId}"));
            await UpdateAllSensorData(token);

            List<SensorData> changed = new List<SensorData>();
            foreach (var data in GetSensorData())
            {
                changed.Add(data.Value.Clone());
            }
            PortsChanged?.Invoke(this, changed);

            var socketsUrl = $"ws://{DeviceIP}:7681/?c={sessionId}";
            webSocket = new WebSocket(socketsUrl, "mfi-protocol");
            webSocket.ReceiveBufferSize = 64 * 1024; // 64k
            webSocket.Opened += WebSocket_Opened;
            webSocket.MessageReceived += WebSocket_MessageReceived;
            webSocket.Error += WebSocket_Error;
            webSocket.Closed += WebSocket_Closed;
            webSocket.Open();
            logger.LogDebug(Invariant($"Created WebSocket to {DeviceIP}"));
        }

        public IDictionary<int, SensorData> GetSensorData(IEnumerable<int> filter = null)
        {
            sensorDataMapLock.EnterReadLock();
            try
            {
                if (filter == null)
                {
                    filter = sensorDataMap.Keys;
                }
                var ret = new Dictionary<int, SensorData>();
                foreach (int port in filter)
                {
                    if (sensorDataMap.TryGetValue(port, out SensorData value))
                    {
                        ret.Add(port, (SensorData)value.Clone());
                    }
                }
                return ret;
            }
            finally
            {
                sensorDataMapLock.ExitReadLock();
            }
        }

        public async Task UpdateOutputDirectly(int port, bool newState, CancellationToken token)
        {
            logger.LogDebug(Invariant($"Updating Port {port} on {DeviceIP} state to {newState}"));

            var putUrl = new Uri($"http://{DeviceIP}/sensors/{port}");
            string formDataString = Invariant($"output={(newState ? 1 : 0)}");
            HttpWebRequest request = CreateFormWebRequest(putUrl, formDataString, "POST");

            string result = await ProcessRequest(request, token).ConfigureAwait(false);

            PutResult data = DeserializeJson<PutResult>(result);
            if (data.Status != "success")
            {
                string error = Invariant($"Failed to update Port:{port} with {data.Status ?? string.Empty}");
                throw new HspiException(error);
            }
            logger.LogDebug(Invariant($"Updated Port {port} on {DeviceIP} state to {newState}"));
        }

        public async Task UpdateOutput(int port, bool newState, CancellationToken token)
        {
            var webSocketCopy = webSocket;
            if (webSocketCopy.State == WebSocketState.Open)
            {
                logger.LogDebug(Invariant($"Updating Port {port} on {DeviceIP} state to {newState}"));
                string jsonString = Invariant($"{{\"sensors\": [ {{ \"port\":{port},  \"output\":{(newState ? 1 : 0)} }}] }}");
                webSocketCopy.Send(jsonString); // it is send async so we don't know what happens

                logger.LogDebug(Invariant($"Updated Port {port} on {DeviceIP} state to {newState}"));
            }
            else
            {
                await UpdateOutputDirectly(port, newState, token);
            }
        }

        public async Task UpdateAllSensorData(CancellationToken token)
        {
            var postUrl = new Uri($"http://{DeviceIP}/sensors");
            HttpWebRequest request = CreateWebRequest(postUrl, "GET");
            string initialData = await ProcessRequest(request, token);
            var sensorsData = DeserializeJson<InitialData>(initialData);
            this.sensorDataMapLock.EnterWriteLock();
            try
            {
                foreach (var data in sensorsData.Sensors)
                {
                    this.sensorDataMap[data.Port] = data;
                }
            }
            catch
            {
                Interlocked.Increment(ref totalErrors);
                throw;
            }
            finally
            {
                this.sensorDataMapLock.ExitWriteLock();
            }
        }

        public async Task LogOut(CancellationToken token)
        {
            logger.LogDebug(Invariant($"Logging out for {DeviceIP} with {sessionId ?? string.Empty}"));
            DisposeWebSocket();

            var logoutUri = new Uri($"http://{DeviceIP}/logout.cgi");
            HttpWebRequest request = CreateWebRequest(logoutUri, "GET");
            await ProcessRequest(request, token);
            logger.LogInfo(Invariant($"Logged out for {DeviceIP} with {sessionId ?? string.Empty}"));
        }

        private void DisposeWebSocket()
        {
            if (webSocket != null)
            {
                webSocket.Close();
                webSocket.Opened -= WebSocket_Opened;
                webSocket.MessageReceived -= WebSocket_MessageReceived;
                webSocket.Error -= WebSocket_Error;
                webSocket.Closed -= WebSocket_Closed;
                webSocket.Dispose();
                webSocket = null;
            }
        }

        private static async Task<string> ProcessRequest(HttpWebRequest request, CancellationToken token)
        {
            using (var response = await GetResponseAsync(request, token).ConfigureAwait(false))
            {
                using (Stream stream = response.GetResponseStream())
                {
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    return await reader.ReadToEndAsync();
                }
            }
        }

        private static async Task<HttpWebResponse> GetResponseAsync(HttpWebRequest request, CancellationToken token)
        {
            using (token.Register(() => request.Abort(), useSynchronizationContext: false))
            {
                var response = await request.GetResponseAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return (HttpWebResponse)response;
            }
        }

        private HttpWebRequest CreateFormWebRequest(Uri uri, string formDataString, string method)
        {
            HttpWebRequest request = CreateWebRequest(uri, method);
            byte[] formData = Encoding.UTF8.GetBytes(formDataString);
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = formData.Length;

            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(formData, 0, formData.Length);
            }

            return request;
        }

        private static T DeserializeJson<T>(string result) where T : class
        {
            using (MemoryStream memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(result)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return serializer.ReadObject(memoryStream) as T;
            }
        }

        private void WebSocket_Closed(object sender, EventArgs e)
        {
            logger.LogDebug(Invariant($"WebSocket Closed out for {DeviceIP} with {sessionId ?? string.Empty}"));
            cancelationTokenSourceForCompleted.Cancel();
        }

        private void WebSocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            logger.LogWarning(Invariant($"WebSocket Errored out for {DeviceIP} with {sessionId ?? string.Empty} with  {e.Exception.Message}"));
            cancelationTokenSourceForCompleted.Cancel();
        }

        private void WebSocket_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                lock (lastMessageLock)
                {
                    lastMessage.Restart();
                }
                var data = DeserializeJson<WebSocketData>(e.Message);
                UpdateDelta(data.Sensors);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref totalErrors);
                logger.LogWarning(Invariant($"Failed to process Websocket data with {ex.Message}"));
            }
        }

        private void WebSocket_Opened(object sender, EventArgs e)
        {
            lock (lastMessageLock)
            {
                lastMessage.Restart();
            }

            webSocket.Send("{\"time\":10}");
        }

        private HttpWebRequest CreateWebRequest(Uri uri, string method)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            request.ServicePoint.Expect100Continue = false;
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.Add(uri, new Cookie("AIROS_SESSIONID", sessionId));
            request.Host = uri.Host;
            request.AllowAutoRedirect = false;
            return request;
        }

        private static string GenerateSessionId()
        {
            // Generates a random 32 digit session ID
            Random random = new Random();
            int length = 32;
            StringBuilder stb = new StringBuilder(length);
            string possible = "0123456789";
            for (int i = 0; i < length; i++)
            {
                stb.Append(possible[random.Next(possible.Length)]);
            }

            return stb.ToString();
        }

        private void UpdateDelta(IEnumerable<SensorData> sensors)
        {
            List<SensorData> changed = new List<SensorData>();
            sensorDataMapLock.EnterWriteLock();
            try
            {
                foreach (var data in sensors)
                {
                    if (sensorDataMap.TryGetValue(data.Port, out var originalData))
                    {
                        originalData.ApplyDelta(data);
                        changed.Add(data.Clone());
                    }
                }
            }
            finally
            {
                sensorDataMapLock.ExitWriteLock();
            }

            PortsChanged?.Invoke(this, changed);
        }

        private readonly string sessionId;
        private WebSocket webSocket;
        private readonly ReaderWriterLockSlim sensorDataMapLock = new ReaderWriterLockSlim();
        private readonly IDictionary<int, SensorData> sensorDataMap = new Dictionary<int, SensorData>();
        private readonly ILogger logger;
        private CancellationTokenSource cancelationTokenSourceForCompleted = new CancellationTokenSource();
        private long totalErrors = 0;
        private Stopwatch lastMessage = new Stopwatch();
        private object lastMessageLock = new object();

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    DisposeWebSocket();
                    sensorDataMapLock.Dispose();
                    cancelationTokenSourceForCompleted.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion IDisposable Support
    }
}