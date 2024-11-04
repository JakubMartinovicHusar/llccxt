using System.Text;

using System;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;

namespace ccxt;


public delegate void HandleJsonStringMessageDelegate(object sender, JsonMessageEventArgs eventArgs);
public delegate void HandleByteArrayMessageDelegate(object sender, ByteArrayMessageEventArgs eventArgs);

public partial class Exchange
{
    public partial class WebSocketClient
    {
        // public delegate void ThresholdReachedEventHandler(ThresholdReachedEventArgs e);
        public event HandleJsonStringMessageDelegate jsonStringMessageReceived;
        public event HandleByteArrayMessageDelegate byteArrayMessageReceived;

        private void TryHandleMessage (string message)
        {
            // Console.WriteLine($"TryHandleMessage: {message}");
            try{
                this.jsonStringMessageReceived?.Invoke(this, new JsonMessageEventArgs(message));
            }
            catch (Exception e)
            {
                 if (this.verbose)
                 {
                    Console.WriteLine($"Error in TryHandleMessage Json: {e.Message}");
                 }
            }
            if (this.handleMessage != null){
                object deserializedMessages = message;
                try
                {
                    deserializedMessages = JsonHelper.Deserialize(message);
                }
                catch (Exception e)
                {
                }
                this.handleMessage(this, deserializedMessages);
            }
        }

        private async Task handleMessageAsync(WebSocketClient client, object messageContent)
        {
            this.handleMessage(this, messageContent);
        }

        private void TryByteArrayMessage (byte[] messageByteArray)
        {
            this.byteArrayMessageReceived?.Invoke(this, new ByteArrayMessageEventArgs(messageByteArray));
        }
    
        private async Task Receiving(ClientWebSocket webSocket)
        {
            var buffer = new byte[1000000]; // check best size later
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    // var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    var memory = new MemoryStream();

                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        memory.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);


                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        // TryByteArrayMessage(memory.ToArray());
                        var message = Encoding.UTF8.GetString(memory.ToArray(), 0, (int)memory.Length);
                        if (this.verbose)
                        {
                            Console.WriteLine($"On message: {message}");
                        }
                        this.TryHandleMessage(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {

                        // Handle binary message
                        // assume gunzip for now

                        using (MemoryStream compressedStream = new MemoryStream(buffer, 0, result.Count))
                        using (GZipStream decompressionStream = new GZipStream(compressedStream, CompressionMode.Decompress))
                        using (MemoryStream decompressedStream = new MemoryStream())
                        {
                            decompressionStream.CopyTo(decompressedStream);
                            byte[] decompressedData = decompressedStream.ToArray();
                            // TryByteArrayMessage(decompressedData);
                            string decompressedString = System.Text.Encoding.UTF8.GetString(decompressedData);

                            if (this.verbose)
                            {
                                Console.WriteLine($"On binary message {decompressedString}");
                            }
                            this.TryHandleMessage(decompressedString);
                        }
                        // string json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        this.onClose(this, null);
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        this.isConnected = false;
                    }
                    // else if (result.MessageType == WebSocketMessageType.Pong)
                    // {
                    //     Console.WriteLine("On Pong message:");
                    //     // Handle the Pong message as needed
                    // }
                }
            }
            catch (Exception ex)
            {
                if (this.verbose)
                {
                    Console.WriteLine($"Receiving error: {ex.Message}");
                }
                this.isConnected = false;
                this.onError(this, ex);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal async Task sendPreparedMessageAsync(byte[] messageBytes)
        {
            var arraySegment = new ArraySegment<byte>(messageBytes, 0, messageBytes.Length);
            await sendAsyncWrapper(this.webSocket, arraySegment,
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None);
        }

    }

}
