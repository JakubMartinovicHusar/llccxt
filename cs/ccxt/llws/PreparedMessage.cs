using System;
using System.Text;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace ccxt;

public interface IPlaceholder
{
    string ExpectedValue { get; }
}

public class Placeholder<T1> : IPlaceholder
{
    public T1 PassedValue {get; private set;}
    public string ExpectedValue {get; private set;}        

    public Placeholder(T1 passedValue, string expectedValue)
    {
        PassedValue = passedValue;
        ExpectedValue = expectedValue;
    }
}

public partial class Exchange
{
    public class PreparedMessage
    {
        public WebSocketClient client;
        public Exchange exchange;
        private string url;
        public Dictionary<string, byte[]> bytesMessageDictionary;
        public object subscription;
        public Dictionary<string, string> queryTemplateDictionary;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Dictionary<string, byte[]> GetPreparedMessageBytesDictionary()
        {
            return this.bytesMessageDictionary.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Dictionary<string, string> GetQueryTemplateDictionary()
        {
            if (this.queryTemplateDictionary == null)
                return null;
            return this.queryTemplateDictionary.ToDictionary(entry => entry.Key, entry => entry.Value);
        }
        
        public PreparedMessage(
            WebSocketClient client,
            Exchange exchange,
            string url, 
            byte[] bytesMessage, 
            Dictionary<string, IPlaceholder> placeholderObjectDict,
            Dictionary<string, object> subscription,
            string queryTemplate = null
        )  
        {
            void AddPlaceholderToPlaceHolderMap(Dictionary<string, Tuple<int, int>> placeholderPositionMap, string placeholderVarName, string placeholderVarString, string valueEnding, string alternativeValueEnding = null){

                var placeholderBytes = Encoding.UTF8.GetBytes(placeholderVarString);
                var placeholderBytesComma = Encoding.UTF8.GetBytes(valueEnding);
                var placeholderBytesCurrlyBracket = Encoding.UTF8.GetBytes(alternativeValueEnding);
                int placeHolderObjectIndex = GetIndexOfSubArray(bytesMessage, placeholderBytes);
                if(placeHolderObjectIndex < 0)
                    return;
                placeHolderObjectIndex += placeholderBytes.Length;
                int placeHolderObjectLength = Math.Min(ReplaceNegWithMax(GetIndexOfSubArray(bytesMessage, placeholderBytesComma, placeHolderObjectIndex)), ReplaceNegWithMax(GetIndexOfSubArray(bytesMessage, placeholderBytesCurrlyBracket, placeHolderObjectIndex))) - placeHolderObjectIndex;

                placeholderPositionMap.Add(placeholderVarName, new Tuple<int, int>(placeHolderObjectIndex, placeHolderObjectLength));
            }

            void AddSignatureToPositionQueryTemplateMap(Dictionary<string, Tuple<int, int>> placeholderPositionQueryTemplateMap, string placeholderVarName, string placeholderVarString){
                int placeHolderStringIndex = queryTemplate.IndexOf(placeholderVarString) + placeholderVarString.Length;
                int length = queryTemplate.IndexOf("&", placeHolderStringIndex);
                if(length < 0)
                    length = queryTemplate.Length;
                length -= placeHolderStringIndex;
                
                placeholderPositionQueryTemplateMap.Add(placeholderVarName, new Tuple<int, int>(placeHolderStringIndex, length));
            }



            int ReplaceNegWithMax(int value)
            {
                return value < 0 ? int.MaxValue : value;
            }

            this.client = client;
            this.exchange = exchange;
            this.url = url;
            this.bytesMessageDictionary =  new Dictionary<string, byte[]>();
            this.subscription = subscription;

            # region Message dictionary construction
            // Filling map that will serve for splitting message into pieces
            Dictionary<string, Tuple<int, int>> placeholderPositionMap = new Dictionary<string, Tuple<int, int>>();
            foreach (var placeholderKeyValuePair in placeholderObjectDict)
            {
                if(placeholderKeyValuePair.Value.ExpectedValue == null)
                    continue;
                var placeholderBytes = Encoding.UTF8.GetBytes(placeholderKeyValuePair.Value.ExpectedValue.ToString());
                // Find index for placeholder
                int placeHolderObjectIndex = GetIndexOfSubArray(bytesMessage, placeholderBytes);
                placeholderPositionMap.Add(placeholderKeyValuePair.Key, new Tuple<int, int>(placeHolderObjectIndex, placeholderBytes.Length));
            }
            // Timestamp 
            AddPlaceholderToPlaceHolderMap(placeholderPositionMap, "timestamp", "timestamp\":", ",", "}");
             // NewClientOrderId 
            AddPlaceholderToPlaceHolderMap(placeholderPositionMap, "newClientOrderId", "newClientOrderId\":\"", "\",", "\"}");
            // Signature
            AddPlaceholderToPlaceHolderMap(placeholderPositionMap, "signature", "signature\":\"", "\",", "\"}");
            
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

            # endregion
            
            # region Query template dictionary construction
            // Breaking down query template searching for placeholders and constructing queryTemplateDictionary
            if(queryTemplate!=null){
                this.queryTemplateDictionary = new Dictionary<string, string>();

                Dictionary<string, Tuple<int, int>> placeholderPositionQueryTemplateMap = new Dictionary<string, Tuple<int, int>>();
                foreach(var placeholder in placeholderObjectDict)
                {
                    var placeHolderExpectedValue = placeholder.Value.ExpectedValue;
                    if(placeHolderExpectedValue == null)
                        continue;
                    int placeHolderObjectIndex = queryTemplate.IndexOf(placeHolderExpectedValue);
                    if(placeHolderObjectIndex < 0)
                        continue;
                    placeholderPositionQueryTemplateMap.Add(placeholder.Key, new Tuple<int, int>(placeHolderObjectIndex, placeHolderExpectedValue.Length));
                }

                AddSignatureToPositionQueryTemplateMap(placeholderPositionQueryTemplateMap, "timestamp", "timestamp=");
                AddSignatureToPositionQueryTemplateMap(placeholderPositionQueryTemplateMap, "newClientOrderId", "newClientOrderId=");

                placeholderPositionQueryTemplateMap = placeholderPositionQueryTemplateMap.OrderBy(kvp => kvp.Value.Item1).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                // Splitting message into pieces
                length = 0;
                coreCount = 0;
                lastPosition = 0;
                foreach(var placeholderPosition in placeholderPositionQueryTemplateMap)
                { 
                    // Preceding core message part is added to bytesMessageDictionary
                    length = placeholderPosition.Value.Item1 - lastPosition;
                    if (length > 0)
                    {
                        string target = queryTemplate.Substring(lastPosition, length);
                        queryTemplateDictionary.Add($"core_{coreCount}", target);
                    }
                    
                    // placeholder message part is added to bytesMessageDictionary
                    queryTemplateDictionary.Add(placeholderPosition.Key, null);
                    lastPosition = placeholderPosition.Value.Item1 + placeholderPosition.Value.Item2;
                    coreCount++;
                }

                // Adding remaining piece of message
                length = queryTemplate.Length - lastPosition;
                if (length > 0)
                {
                    string target = queryTemplate.Substring(lastPosition, length);
                    queryTemplateDictionary.Add($"core_{coreCount}", target);
                }
            }

            # endregion
        }
        
        private int GetIndexOfSubArray(byte[] array, byte[] subArray, int startIdx = 0)
        {
            for (int i = startIdx; i < array.Length - subArray.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < subArray.Length; j++)
                {
                    if (array[i + j] != subArray[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
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
        protected virtual async Task<byte[]> CombineGetSignatureTemplateQuery(IEnumerable<string> queryList){
            return await this.exchange.GetSignatureFromTemplateQuery(String.Concat(queryList));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<object> SendAsync(Dictionary<string, string> messageData){
            
            Task<byte[]> signature = null;
            messageData.Add("timestamp", exchange.milliseconds().ToString());
            // Signature
            var preBuiltQueryTemplateDictionary = this.GetQueryTemplateDictionary();
            if(preBuiltQueryTemplateDictionary != null){
                foreach (var kvp in messageData)
                {
                    preBuiltQueryTemplateDictionary[kvp.Key] = kvp.Value;
                }
                signature = this.CombineGetSignatureTemplateQuery(preBuiltQueryTemplateDictionary.Values);
            }
            // Rest of the message

            var requestId = this.exchange.GetRequestId(url);
            string messageHash = this.getMessageHash(requestId);
            messageData.Add("messageHash", messageHash);
            
            string subscribeHash = messageHash;

            var preBuiltMessageBytes = this.GetPreparedMessageBytesDictionary();
            foreach (var kvp in messageData)
            {
                preBuiltMessageBytes[kvp.Key] = Encoding.UTF8.GetBytes(kvp.Value);
            }
            if(signature != null)
                preBuiltMessageBytes["signature"] = await signature;
            var message = CombineAsync(preBuiltMessageBytes.Values.ToArray());
            
            // Combine all the pieces
            var future = (client.futures as ConcurrentDictionary<string, Future>).GetOrAdd(messageHash, (key) => client.future(messageHash));
            if (subscribeHash == null) {
                return await future;
            }
            var connected = client.connect(0);
            if ((client.subscriptions as ConcurrentDictionary<string, object>).TryAdd(subscribeHash, this.subscription ?? true))
            {
                await connected;
                if (await message != null)
                {
                    try
                    {
                        this.client.sendPreparedMessageAsync(await message);
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
        public object SendSync(Dictionary<string, string> messageData){
            
            messageData.Add("timestamp", exchange.milliseconds().ToString());
            // Signature
            var preBuiltQueryTemplateDictionary = this.GetQueryTemplateDictionary();
            foreach (var kvp in messageData)
            {
                preBuiltQueryTemplateDictionary[kvp.Key] = kvp.Value;
            }
            var signature = this.CombineGetSignatureTemplateQuery(preBuiltQueryTemplateDictionary.Values);

            // Rest of the message

            var requestId = this.exchange.GetRequestId(url);
            string messageHash = this.getMessageHash(requestId);
            messageData.Add("messageHash", messageHash);
            
            string subscribeHash = messageHash;

            var preBuiltMessageBytes = this.GetPreparedMessageBytesDictionary();
            foreach (var kvp in messageData)
            {
                preBuiltMessageBytes[kvp.Key] = Encoding.UTF8.GetBytes(kvp.Value);
            }
            signature.Wait();
            preBuiltMessageBytes["signature"] = signature.Result;
            
            // Combine all the pieces
            var future = (client.futures as ConcurrentDictionary<string, Future>).GetOrAdd((string)messageHash, (key) => client.future(messageHash));

            if (subscribeHash == null) {
                future.task.Wait();
                return future.task.Result;
            }
            
            var message = this.Combine(preBuiltMessageBytes.Values.ToArray());
            var connected = client.connect(0);
            if ((client.subscriptions as ConcurrentDictionary<string, object>).TryAdd(subscribeHash, this.subscription ?? true))
            {
                connected.Wait();
                if (message != null)
                {
                    try
                    {
                        this.client.sendPreparedMessageAsync(message);
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

        protected virtual string GetSignatureSync(object parameters){
            throw new NotImplementedException("This method has to be implemented on exchange level.");
        }

        protected async Task<string> GetSignatureAsync(object parameters){
            return GetSignatureSync(parameters);
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
            Dictionary<string, IPlaceholder> placeholderObjectDict,
            Dictionary<string, object> subscription,
            string queryTemplate = null
        )  : base(client, exchange, url, bytesMessage, placeholderObjectDict, subscription, queryTemplate)
        {
        }


        public async Task<ccxt.Order> SendAsyncGetOrder(string symbol, string type, string side, double amount, double price, string parameters){
            var message = await SendAsync(symbol, type, side, amount, price, parameters);
            return new ccxt.Order();
        }
            

        public object SendSync(string symbol, string type, string side, double amount, double price, string parameters){
            // [asyncEncoding.UTF8.GetBytes(kvp.Value.ToString())
            Dictionary<string, string> messageData = new Dictionary<string, string>();
            messageData.Add("symbol", symbol);
            messageData.Add("type", type);
            messageData.Add("side", side);
            messageData.Add("amount", amount.ToString());
            messageData.Add("price", price.ToString());
            messageData.Add("newClientOrderId", exchange.uuid22());
            if(parameters != null)
                messageData.Add("parameters", parameters.ToString());
            return base.SendSync(
                messageData
            );
        }

        public async Task<object> SendAsync(string symbol, string type, string side, double amount, double price, string parameters){
            // [asyncEncoding.UTF8.GetBytes(kvp.Value.ToString())
            Dictionary<string, string> messageData = new Dictionary<string, string>();
            messageData.Add("symbol", symbol);
            messageData.Add("type", type);
            messageData.Add("side", side);
            messageData.Add("amount", amount.ToString());
            messageData.Add("price", price.ToString()); // Encoding.UTF8.GetBytes(
            messageData.Add("newClientOrderId", exchange.uuid22());
            if(parameters != null)
                messageData.Add("parameters", parameters.ToString());
            return await base.SendAsync(
                messageData
            );

        }
    }


    public class PreparedEditOrderMessage : PreparedMessage
    {
        public PreparedEditOrderMessage(
            WebSocketClient client,
            Exchange exchange,
            string url, 
            byte[] bytesMessage, 
            Dictionary<string, IPlaceholder> placeholderObjectDict,
            Dictionary<string, object> subscription,
            string queryTemplate = null
        )  : base(client, exchange, url, bytesMessage, placeholderObjectDict, subscription, queryTemplate)
        {
        }
        
        public object SendSync(string orderId, string symbol, string type, string side, double amount, double price, string parameters){
            // [asyncEncoding.UTF8.GetBytes(kvp.Value.ToString())
            Dictionary<string, string> messageData = new Dictionary<string, string>();
            messageData.Add("orderId", orderId);
            messageData.Add("symbol", symbol);
            messageData.Add("type", type);
            messageData.Add("side", side);
            messageData.Add("amount", amount.ToString());
            messageData.Add("price", price.ToString());
            messageData.Add("newClientOrderId", exchange.uuid22());
            if(parameters != null)
                messageData.Add("parameters", parameters.ToString());
            return base.SendSync(
                messageData
            );
        }

        public async Task<object> SendAsync(string orderId, string symbol, string type, string side, double amount, double price, string parameters){
            // [asyncEncoding.UTF8.GetBytes(kvp.Value.ToString())
            Dictionary<string, string> messageData = new Dictionary<string, string>();
            messageData.Add("orderId", orderId);
            messageData.Add("symbol", symbol);
            messageData.Add("type", type);
            messageData.Add("side", side);
            messageData.Add("amount", amount.ToString());
            messageData.Add("price", price.ToString());
            messageData.Add("newClientOrderId", exchange.uuid22());
            if(parameters != null)
                messageData.Add("parameters", parameters.ToString());
            return await base.SendAsync(
                messageData
            );

        }
    }


    public class PreparedCancelOrderMessage : PreparedMessage {

        public PreparedCancelOrderMessage(
            WebSocketClient client,
            Exchange exchange,
            string url, 
            byte[] bytesMessage, 
            Dictionary<string, IPlaceholder> placeholderObjectDict,
            Dictionary<string, object> subscription,
            string queryTemplate = null
        )  : base(client, exchange, url, bytesMessage, placeholderObjectDict, subscription,queryTemplate)
        {
        }
        
        public object SendSync(string orderId){
            // [asyncEncoding.UTF8.GetBytes(kvp.Value.ToString())
            Dictionary<string, string> messageData = new Dictionary<string, string>();
            messageData.Add("orderId", orderId);
            return base.SendSync(
                messageData
            );
        }

        public async Task<object> SendAsync(string orderId){
            // [asyncEncoding.UTF8.GetBytes(kvp.Value.ToString())
            Dictionary<string, string> messageData = new Dictionary<string, string>();
            messageData.Add("orderId", orderId);
            return await base.SendAsync(
                messageData
            );

        }
    }
}