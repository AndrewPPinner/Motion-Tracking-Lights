using System.Net.WebSockets;
using System.Security.Cryptography.Xml;
using OpenCvSharp;

namespace VideoProcessingServer.Handlers
{

    public static class WebSocketHandler
    {
        private static List<WebSocket> _subscribers = new();
        private static BackgroundSubtractorKNN _subtractor;

        public static async Task HandleStream(WebSocket webSocket, BackgroundSubtractorKNN subtractor)
        {
            if (_subtractor == null)
            {
                _subtractor = subtractor;
            }
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
            _subtractor.DetectShadows = true;

            try
            {
                using var currFrame = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                if (currFrame.Empty()) return null;

                Cv2.GaussianBlur(currFrame, currFrame, new Size(5, 5), 0);

                Mat fgMask = new Mat();
                _subtractor.Apply(currFrame, fgMask, 0);

                // Clean the mask
                Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
                Cv2.MorphologyEx(fgMask, fgMask, MorphTypes.Close, kernel);
                Cv2.MorphologyEx(fgMask, fgMask, MorphTypes.Open, kernel);
                Cv2.Threshold(fgMask, fgMask, 200, 255, ThresholdTypes.Binary);

                // Find only the largest contour
                Point[][] contours;
                HierarchyIndex[] hierarchy;
                Cv2.FindContours(fgMask, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                using var res = Cv2.ImDecode(imageBytes, ImreadModes.Color);

                double maxArea = 0;
                Point[]? largestContour = null;

                foreach (var contour in contours)
                {
                    double area = Cv2.ContourArea(contour);
                    if (area > maxArea)
                    {
                        maxArea = area;
                        largestContour = contour;
                    }
                }

                if (largestContour != null && maxArea > 500)
                {
                    Rect bbox = Cv2.BoundingRect(largestContour);
                    Cv2.Rectangle(res, bbox, Scalar.Red, 2);
                }

                return res.ToBytes(".jpg");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Frame processing failed: " + ex.Message);
                return null;
            }
        }
    }
}

