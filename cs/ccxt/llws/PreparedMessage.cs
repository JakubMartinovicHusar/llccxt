using System;
using System.Text;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace ccxt;

public partial class Exchange
{
    public class PreparedMessage
    {
        public WebSocketClient client;
        public Exchange exchange;
        private string url;
        public Dictionary<string, byte[]> bytesMessageDictionary;
        public object subscription;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Dictionary<string, byte[]> GetPreparedMessageBytesDictionary()
        {
            return new Dictionary<string, byte[]>(this.bytesMessageDictionary);
        }
        
        public PreparedMessage(
            WebSocketClient client,
            Exchange exchange,
            string url, 
            byte[] bytesMessage, 
            Dictionary<string, object> placeholderObjectDict,
            Dictionary<string, object> subscription
        )  
        {
            this.client = client;
            this.exchange = exchange;
            this.url = url;
            this.bytesMessageDictionary =  new Dictionary<string, byte[]>();
            this.subscription = subscription;
            // Filling map that will serve for splitting message into pieces
            Dictionary<string, Tuple<int, int>> placeholderPositionMap = new Dictionary<string, Tuple<int, int>>();
            foreach (var placeholderKeyValuePair in placeholderObjectDict)
            {
                var placeholderBytes = Encoding.UTF8.GetBytes(placeholderKeyValuePair.Value.ToString());
                // Find index for placeholder
                placeholderPositionMap.Add(placeholderKeyValuePair.Key, new Tuple<int, int>(Array.IndexOf(bytesMessage, placeholderBytes), placeholderBytes.Length));
            }

            // Ordering by position
            placeholderPositionMap = placeholderPositionMap.OrderBy(kvp => kvp.Value.Item1).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            // Splitting message into pieces
            int length = 0;
            int coreCount = 0;
            int lastPosition = 0;
            foreach(var placeholderPosition in placeholderPositionMap)
            {
                // Preceding core message part is added to bytesMessageDictionary
                length = placeholderPosition.Value.Item1 - lastPosition;
                if (length > 0)
                {
                    byte[] target = new byte[length];
                    Array.Copy(bytesMessage, lastPosition, target, 0, length);
                    bytesMessageDictionary.Add($"core_{coreCount}", target);
                }
                
                // placeholder message part is added to bytesMessageDictionary
                bytesMessageDictionary.Add(placeholderPosition.Key, null);
                lastPosition = placeholderPosition.Value.Item1 + placeholderPosition.Value.Item2;
                coreCount++;
            }

            // Adding remaining piece of message
            length = bytesMessage.Length - lastPosition;
            if (length > 0)
            {
                byte[] target = new byte[length];
                Array.Copy(bytesMessage, lastPosition, target, 0, length);
                bytesMessageDictionary.Add($"core_{coreCount}", target);
            }
        }

        private string getMessageHash(object requestId)
        {
            string messageHash = requestId.ToString();
            return messageHash;
        }

    #region Fast methods for message dispatching
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] Combine<T>(params T[][] arrays)
        {
                T[] ret = new T[arrays.Sum(x => x.Length)];
                int offset = 0;
                foreach (T[] data in arrays.AsSpan())
                {
                    Buffer.BlockCopy(data, 0, ret, offset, data.Length);
                    offset += data.Length;
                }
                return ret;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T[]> CombineAsync<T>(params T[][] arrays)
        {
                T[] ret = new T[arrays.Sum(x => x.Length)];
                int offset = 0;
                foreach (T[] data in arrays)
                {
                    Buffer.BlockCopy(data, 0, ret, offset, data.Length);
                    offset += data.Length;
                }
                return ret;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<object> SendAsync(object requestId, Dictionary<string, byte[]> messageData){
            
            string messageHash = this.getMessageHash(requestId);
            messageData.Add("messageHash", Encoding.UTF8.GetBytes(messageHash));
            string subscribeHash = messageHash;

            var preBuiltMessageBytes = this.GetPreparedMessageBytesDictionary();
            var message = CombineAsync(preBuiltMessageBytes.Values.ToArray());
            foreach (var kvp in messageData)
            {
                preBuiltMessageBytes[kvp.Key] = kvp.Value;
            }
            // Combine all the pieces
            var future = (client.futures as ConcurrentDictionary<string, Future>).GetOrAdd (messageHash, (key) => client.future(messageHash));
            if (subscribeHash == null) {
                return await future;
            }
            // var connected = client.connect(0);
            if ((client.subscriptions as ConcurrentDictionary<string, object>).TryAdd(subscribeHash, this.subscription ?? true))
            {
                //await connected;
                if (message != null)
                {
                    try
                    {
                        await this.client.sendPreparedMessageAsync(await message);
                    }
                    catch (Exception ex)
                    {
                        client.subscriptions.Remove((string)subscribeHash);
                        future.reject(ex);
                        // future.SetException(ex); check this out
                    }
                }
            }
            return await future;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object SendSync(object requestId, Dictionary<string, byte[]> messageData){
            
            string messageHash = this.getMessageHash(requestId);
            messageData.Add("messageHash", Encoding.UTF8.GetBytes(messageHash));
            string subscribeHash = messageHash;
            
            var preBuiltMessageBytes = this.GetPreparedMessageBytesDictionary();
            var message = Combine(preBuiltMessageBytes.Values.ToArray());
            foreach (var kvp in messageData)
            {
                preBuiltMessageBytes[kvp.Key] = kvp.Value;
            }
            // Combine all the pieces
            var future = (client.futures as ConcurrentDictionary<string, Future>).GetOrAdd((string)messageHash, (key) => client.future(messageHash));

            if (subscribeHash == null) {
                future.task.Wait();
                return future.task.Result;
            }
            // var connected = client.connect(0);
            if ((client.subscriptions as ConcurrentDictionary<string, object>).TryAdd(subscribeHash, this.subscription ?? true))
            {
                // connected.Wait();
                if (message != null)
                {
                    try
                    {
                        this.client.sendPreparedMessageSync(message);
                    }
                    catch (Exception ex)
                    {
                        client.subscriptions.Remove(subscribeHash);
                        future.reject(ex);
                        // future.SetException(ex); check this out
                    }
                }
            }
            future.task.Wait();
            return (object)future.task.Result;
        }

    #endregion
    }

    public class PreparedCreateOrderMessage : PreparedMessage
    {

        public PreparedCreateOrderMessage(
            WebSocketClient client,
            Exchange exchange,
            string url, 
            byte[] bytesMessage, 
            Dictionary<string, object> placeholderObjectDict,
            Dictionary<string, object> subscription
        )  : base(client, exchange, url, bytesMessage, placeholderObjectDict, subscription)
        {
        }
        
        public object SendSync(object requestId, string symbol, string type, string side, double amount, double price){
            // [asyncEncoding.UTF8.GetBytes(kvp.Value.ToString())
            Dictionary<string, byte[]> messageData = new Dictionary<string, byte[]>();
            messageData.Add("symbol", Encoding.UTF8.GetBytes(symbol.ToString()));
            messageData.Add("type", Encoding.UTF8.GetBytes(type.ToString()));
            messageData.Add("side", Encoding.UTF8.GetBytes(side.ToString()));
            messageData.Add("amount", Encoding.UTF8.GetBytes(amount.ToString()));
            messageData.Add("price", Encoding.UTF8.GetBytes(price.ToString()));
            return base.SendSync(
                requestId,
                messageData
            );
        }

        public async Task<object> SendAsync(object requestId, string symbol, string type, string side, double amount, double price){
            // [asyncEncoding.UTF8.GetBytes(kvp.Value.ToString())
            Dictionary<string, byte[]> messageData = new Dictionary<string, byte[]>();
            messageData.Add("symbol", Encoding.UTF8.GetBytes(symbol.ToString()));
            messageData.Add("type", Encoding.UTF8.GetBytes(type.ToString()));
            messageData.Add("side", Encoding.UTF8.GetBytes(side.ToString()));
            messageData.Add("amount", Encoding.UTF8.GetBytes(amount.ToString()));
            messageData.Add("price", Encoding.UTF8.GetBytes(price.ToString()));
            return await base.SendAsync(
                requestId,
                messageData
            );

        }
    }
}