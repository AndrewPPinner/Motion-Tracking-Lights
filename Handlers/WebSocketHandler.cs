using System.Net.WebSockets;
using OpenCvSharp;

namespace VideoProcessingServer.Handlers
{

    public static class WebSocketHandler
    {
        private static List<WebSocket> _subscribers = new();
        private static BackgroundSubtractorMOG2 _bgSubtractor = BackgroundSubtractorMOG2.Create();
        private static object _lock = new();

        public static async Task HandleStream(WebSocket webSocket)
        {
            _subscribers.Add(webSocket);
            var buffer = new byte[8192];

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    byte[] imageBytes = ms.ToArray();


                    byte[] processedFrame = ProcessFrame(imageBytes);

                    if (processedFrame == null)
                        continue;

                    // Broadcast processed frame
                    foreach (var subscriber in _subscribers.ToList())
                    {
                        if (subscriber.State == WebSocketState.Open)
                        {
                            await subscriber.SendAsync(new ArraySegment<byte>(processedFrame), WebSocketMessageType.Binary, true, CancellationToken.None);
                        }
                    }
                }
            }
            finally
            {
                _subscribers.Remove(webSocket);
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
            }
        }

        //Update to return some kind of coordinates
        private static byte[]? ProcessFrame(byte[] imageBytes)
        {
            try
            {
                using var mat = Cv2.ImDecode(imageBytes, ImreadModes.AnyColor);
                if (mat.Empty()) return null;

                using var fgMask = new Mat();
                lock (_lock) // Background subtractor isn't thread-safe
                {
                    _bgSubtractor.Apply(mat, fgMask);
                }

                // Find motion contours
                Cv2.FindContours(fgMask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                foreach (var contour in contours)
                {
                    double area = Cv2.ContourArea(contour);
                    if (area < 500) continue; // Filter noise

                    var rect = Cv2.BoundingRect(contour);
                    Cv2.Rectangle(mat, rect, Scalar.Red, 2);
                }
                fgMask.SaveImage("C:\\Users\\andpp\\Downloads\\test.jpeg");
                return mat.ToBytes(".jpg"); // Re-encode processed frame as JPEG
            }
            catch (Exception ex)
            {
                Console.WriteLine("Frame processing failed: " + ex.Message);
                return null;
            }
        }
    }
}

