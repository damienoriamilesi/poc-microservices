/*The MIT License (MIT)

Copyright (c) 2016 Jayamine Alupotha

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.*/

//@author: Jayamine Alupotha

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace EventBusVertx
{
    //This is the structure for JSON message
    internal struct JsonMessage
    {
        private string _type;
        private string _address;
        private string _replyAddress;
        private JObject _body;

        private JObject _headers;

        //create
        public void Create(string newType, string newAddress, string newReplyAddress, JObject newBody)
        {
            _type = newType ?? throw new ArgumentException("JsonMessage:type cannot be null");
            _address = newAddress ?? throw new ArgumentException("JsonMessage:address cannot be null");
            _replyAddress = newReplyAddress;
            _body = newBody;
        }

        //to string
        public string GetMessage()
        {
            if (_replyAddress == null) _replyAddress = "null";
            var jsonMessage = new JObject
            {
                {"type", _type},
                {"address", _address},
                {"replyAddress", _replyAddress},
                {"body", _body},
                {"headers", _headers}
            };
            return jsonMessage.ToString();
        }

        public void SetHeaders(Headers h)
        {
            _headers = h.GetHeaders();
        }
    }

    //This is the structure for headers
    public struct Headers
    {
        private JObject _headers;

        public void AddHeaders(string headerName, string header)
        {
            _headers = new JObject { { headerName, header } };
        }

        //delete headers
        public void DeleteHeaders()
        {
            _headers = null;
        }

        public JObject GetHeaders()
        {
            return _headers;
        }
    }

    //This is the structure for replyHandlers
    public struct ReplyHandlers
    {
        private Action<bool, JObject> _function;
        public string Address;

        public ReplyHandlers(string address, Action<bool, JObject> func)
        {
            _function = func;
            Address = address;
        }

        public void Handle(bool error, JObject message)
        {
            _function(error, message);
        }

        public bool IsNull()
        {
            if (_function == null && Address == null) return true;
            return false;
        }

        public void SetNull()
        {
            _function = null;
            Address = null;
        }
    }

    //This is the structure for Handlers
    public struct Handlers
    {
        private readonly Action<JObject> _function;
        private string _address;

        public Handlers(string address, Action<JObject> func)
        {
            _address = address;
            _function = func;
        }

        public void Handle(JObject message)
        {
            _function(message);
        }
    }

    /*Eventbus constructor
    #	input parameters
    #		1) host	- String
    #		2) port	- integer(>2^10-1)
    #		3) TimeOut - int- receive TimeOut
    #	inside parameters
    #		1) socket
    #		2) handlers - List<address,Handlers> 
    #		3) state -integer
    #		4) ReplyHandler - <address,function>
    #       5) fileLock - object
    #Eventbus state
    #	0 - not connected/failed
    #	1 - connecting
    #	2 - connected /open
    #	3 - closing
    #	4 - closed*/
    public class Eventbus
    {
        private static readonly object FileLock = new object();
        private bool _clearReplyHandler;
        private readonly Dictionary<string, List<Handlers>> _handlers = new Dictionary<string, List<Handlers>>();
        private readonly object _lock = new object();
        private ReplyHandlers _replyHandler;
        private Socket _socket;// = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private int _state;
        private int _timeOut;

        ////constructor
        //public Eventbus(string host = "127.0.0.1", int port = 7000, int TimeOut = 1000)
        //{
        //    _timeOut = TimeOut < 500 ? 500 : TimeOut;

        //    //connect
        //    try
        //    {
        //        _state = 1;
        //        var ipaddress = IPAddress.Parse(host);
        //        var remoteEndPoint = new IPEndPoint(ipaddress, port);
        //        _socket.Connect(remoteEndPoint);
        //        var t = new Thread(Receive);
        //        t.Start();
        //        _state = 2;
        //    }
        //    catch (Exception e)
        //    {
        //        _state = 4;
        //        PrintError(1, "Could not establish the connection\n" + e);
        //        throw new Exception("Not connected " + host + " " + port + "\n", e);
        //    }
        //}
        //Connection send and receive--------------------------------------------------------------------

        public bool IsConnected()
        {
            return _state == 2;
        }

        private bool SendFrame(JsonMessage jsonMessage)
        {
            try
            {
                var message_s = jsonMessage.GetMessage();
                var utf8 = new UTF8Encoding();
                var headerBuffer = new byte[4];
                var bodyBuffer = utf8.GetBytes(message_s);
                var bodyLength = bodyBuffer.Length;
                headerBuffer = BitConverter.GetBytes((uint)bodyLength);
                if (BitConverter.IsLittleEndian) Array.Reverse(headerBuffer);

                // The message might not be sent all at once, but get split up into chunks.
                // If we don't want to sent an incomplete message, we have to loop over the send requests. 
                var bytesSentHeader = 0;
                while (bytesSentHeader < 4)
                    bytesSentHeader += _socket.Send(headerBuffer, bytesSentHeader, 4 - bytesSentHeader, SocketFlags.None);

                // The message might not be sent all at once, but get split up into chunks.
                // This happens often if the message is large.
                // If we don't want to sent an incomplete message, we have to loop over the send requests. 
                var bytesSentBody = 0;
                while (bytesSentBody < bodyLength)
                    bytesSentBody += _socket.Send(bodyBuffer, bytesSentBody, bodyLength - bytesSentBody, SocketFlags.None);
                return true;
            }
            catch (Exception e)
            {
                PrintError(2, "Can not send the message\n" + e);
                throw new Exception("Can not send the message\n", e);
            }
        }

        private void Receive()
        {
            while (true)
                try
                {
                    if (_socket.Poll(_timeOut, SelectMode.SelectRead))
                    {
                        //check state
                        if (_state == 2)
                        {
                            var utf8 = new UTF8Encoding();
                            var lengthBuffer = new byte[4];

                            var receivedBytesLengthBuffer = 0;
                            // If the message is large, it might be sent in several chunks.
                            // We have to collect all the chunks, otherwise we get an incomplete message.
                            // Normally packages are bigger than 4 bytes, so this is just to be sure. 
                            while (receivedBytesLengthBuffer < 4)
                                receivedBytesLengthBuffer += _socket.Receive(lengthBuffer, receivedBytesLengthBuffer,
                                    4 - receivedBytesLengthBuffer, SocketFlags.None);

                            if (BitConverter.IsLittleEndian) Array.Reverse(lengthBuffer);

                            var messageSize = BitConverter.ToInt32(lengthBuffer, 0);

                            var receiveBuffer = new byte[messageSize];

                            var receivedBytesRecvBuff = 0;
                            // If the message is large, it might be sent in several chunks.
                            // We have to collect all the chunks, otherwise we get an incomplete message which results in a JSON parsing exception.
                            // This is NOT so uncommon, if the serialized JSON-Objects are large.
                            while (receivedBytesRecvBuff < messageSize)
                                receivedBytesRecvBuff += _socket.Receive(receiveBuffer, receivedBytesRecvBuff,
                                    messageSize - receivedBytesRecvBuff, SocketFlags.None);

                            var message_string = utf8.GetString(receiveBuffer, 0, receivedBytesRecvBuff);
                            var message = JObject.Parse(message_string);
                            if (message.GetValue("type").ToString() == "message")
                            {
                                var address = message.GetValue("address").ToString();

                                if (address == null)
                                    PrintError(3, "Failed Message\n" + message);
                                else
                                    lock (_lock)
                                    {
                                        //handlers 
                                        if (_handlers.ContainsKey(address))
                                        {
                                            foreach (var handler in _handlers[address]) handler.Handle(message);
                                            //reply address
                                            if (_replyHandler.IsNull() == false)
                                                if (_replyHandler.Address.Equals(address))
                                                {
                                                    _replyHandler.Handle(false, message);
                                                    _replyHandler.SetNull();
                                                    _clearReplyHandler = true;
                                                }
                                        }
                                        //reply handler
                                        else if (_replyHandler.IsNull() == false)
                                        {
                                            if (_replyHandler.Address.Equals(address))
                                            {
                                                _replyHandler.Handle(false, message);
                                                _replyHandler.SetNull();
                                                _clearReplyHandler = true;
                                            }
                                            else
                                            {
                                                PrintError(3, "No handlers to handle this message\n" + message);
                                            }
                                        }
                                        else
                                        {
                                            PrintError(3, "No handlers to handle this message\n" + message);
                                        }
                                    }
                            }

                            if (message.GetValue("type").ToString() == "err")
                            {
                                if (_replyHandler.IsNull() == false)
                                {
                                    _replyHandler.Handle(true, message);
                                    _replyHandler.SetNull();
                                    _clearReplyHandler = true;
                                }
                                else
                                {
                                    PrintError(3, "No handlers to handle this message\n" + message);
                                }
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                    else if (_socket.Poll(100, SelectMode.SelectError))
                    {
                        PrintError(4, "Error at socket polling");
                    }
                }
                catch (Exception e)
                {
                    PrintError(5, e.ToString());
                }
        }

        public void CloseConnection(int timeInterval)
        {
            if (_state == 1)
                _socket.Shutdown(SocketShutdown.Both);
            else
                try
                {
                    Thread.Sleep(timeInterval * 1000);
                    _state = 3;
                    _socket.Shutdown(SocketShutdown.Both);
                    _state = 4;
                }
                catch (Exception e)
                {
                    PrintError(6, e.ToString());
                }
        }
        //send, receive, register, unregister ------------------------------------------------------------

        /*
        #address-string
        #body - json object
        #headers- struct Headers
        #replyAddress - string
        */
        public void Send(string address, JObject body, string replyAddress, Headers headers)
        {
            var message = new JsonMessage();
            message.Create("send", address, replyAddress, body);
            message.SetHeaders(headers);

            while (true)
                if (_socket.Poll(_timeOut, SelectMode.SelectWrite))
                {
                    try
                    {
                        SendFrame(message);
                    }
                    catch (Exception e)
                    {
                        PrintError(7, e.ToString());
                        throw new Exception("", e);
                    }

                    break;
                }
        }

        /*
        #address-string
        #body - json object
        #headers- struct Headers
        #replyAddress - string
        #replyHandler - ReplyHandler
        #timeInterval - int -sec
        */
        public void Send(string address, JObject body, string replyAddress, Headers headers, ReplyHandlers replyHandler,
            int timeInterval = 10)
        {
            var message = new JsonMessage();
            message.Create("send", address, replyAddress, body);
            message.SetHeaders(headers);
            this._replyHandler = replyHandler;
            while (true)
                if (_socket.Poll(_timeOut, SelectMode.SelectWrite))
                {
                    try
                    {
                        SendFrame(message);
                    }
                    catch (Exception e)
                    {
                        PrintError(7, e.ToString());
                        throw new Exception("", e);
                    }

                    break;
                }

            while (timeInterval > 0)
            {
                Thread.Sleep(1000);
                lock (_lock)
                {
                    if (_clearReplyHandler) break;
                    timeInterval--;
                }
            }

            if (timeInterval == 0)
            {
                var err = new JObject();
                err.Add("message", "TIMEOUT ERROR");
                replyHandler.Handle(true, new JObject());
            }
        }

        /*
        #address-string
        #body - json object
        #headers
        */
        public void Publish(string address, JObject body, Headers headers)
        {
            var message = new JsonMessage();
            message.Create("publish", address, null, body);
            message.SetHeaders(headers);

            while (true)
                if (_socket.Poll(_timeOut, SelectMode.SelectWrite))
                {
                    try
                    {
                        SendFrame(message);
                    }
                    catch (Exception e)
                    {
                        PrintError(8, e.ToString());
                        throw new Exception("", e);
                    }

                    break;
                }
        }


        /*
        #address-string
        #headers
        #handler -Handlers
        */
        public void register(string address, Handlers handler)
        {
            if (_handlers.ContainsKey(address) == false)
            {
                var message = new JsonMessage();
                message.Create("register", address, null, null);

                while (true)
                    if (_socket.Poll(_timeOut, SelectMode.SelectWrite))
                    {
                        try
                        {
                            SendFrame(message);
                        }
                        catch (Exception e)
                        {
                            PrintError(9, e.ToString());
                            throw new Exception("", e);
                        }

                        break;
                    }

                var list = new List<Handlers>();
                list.Add(handler);
                _handlers.Add(address, list);
            }
            else
            {
                var handlers = _handlers[address];
                handlers.Add(handler);
                _handlers.Add(address, handlers);
            }
        }

        /*
        #address-string
        #headers - Headers
        */
        public void unregister(string address)
        {
            if (_handlers.ContainsKey(address) == false)
            {
                var message = new JsonMessage();
                message.Create("unregister", address, null, null);

                while (true)
                    if (_socket.Poll(_timeOut, SelectMode.SelectWrite))
                    {
                        try
                        {
                            SendFrame(message);
                        }
                        catch (Exception e)
                        {
                            PrintError(10, e.ToString());
                            throw new Exception("", e);
                        }

                        break;
                    }
            }
            else
            {
                _handlers.Remove(address);
            }
        }
        //Errors ------------------------------------------------------------------------------------------

        public static void PrintError(int code, string error)
        {
            lock (FileLock)
            {
                var name = "error_log_.txt";
                try
                {
                    using (var aFile = new FileStream(name, FileMode.Append, FileAccess.Write))
                    using (var log = new StreamWriter(aFile))
                    {
                        log.WriteLine("********** " + DateTime.Now + " **********\n");
                        log.WriteLine("CODE: " + code + "\n");
                        log.WriteLine(error + "\n\n");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Could not write to the log file\n" + e);
                }
            }
        }

        public Socket TryConnect(string host = "127.0.0.1", int port = 7000, int timeOut = 1000)
        {
            _timeOut = timeOut < 500 ? 500 : timeOut;

            try
            {
                _state = 1;
                var ipaddress = IPAddress.Parse(host);
                var remoteEndPoint = new IPEndPoint(ipaddress, port);
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.Connect(remoteEndPoint);
                var t = new Thread(Receive);
                t.Start();
                _state = 2;
            }
            catch (SocketException e)
            {
                _state = 4;
                PrintError(1, "Socket issue\n" + e);
                throw;
            }
            catch (Exception e)
            {
                _state = 4;
                PrintError(1, "Could not establish the connection\n" + e);
                throw new Exception("Not connected " + host + " " + port + "\n", e);
            }

            return _socket;
        }
    }
}