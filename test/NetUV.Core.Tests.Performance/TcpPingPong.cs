﻿// Copyright (c) Johnny Z. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace NetUV.Core.Tests.Performance
{
    using System;
    using System.Text;
    using NetUV.Core.Buffers;
    using NetUV.Core.Handles;

    sealed class TcpPingPong : IDisposable
    {
        const string PingMessage = "PING";
        const char SplitToken = '\n';
        const int DurationInMilliseconds = 5000;

        WritableBuffer dataBuffer;
        EchoServer server;
        Loop loop;

        long startTime;
        int pongs;
        int state;

        public TcpPingPong()
        {
            byte[] content = Encoding.UTF8.GetBytes(PingMessage + SplitToken);
            this.dataBuffer = WritableBuffer.From(content);
        }

        public void Run()
        {
            this.pongs = 0;
            this.state = 0;
            this.server = new EchoServer(HandleType.Tcp);

            this.loop = this.server.Loop;
            this.StartClient();
        }

        void StartClient()
        {
            Tcp tcp = this.loop
                .CreateTcp()
                .ConnectTo(TestHelper.LoopbackEndPoint, this.OnConnected);

            this.startTime = this.loop.Now;
            this.loop.RunDefault();

            long count = (long)Math.Floor((1000d * this.pongs) / DurationInMilliseconds);
            Console.WriteLine($"Tcp ping pong : {TestHelper.Format(count)} roundtrips/s");

            tcp.Dispose();
        }

        void OnConnected(Tcp tcp, Exception error)
        {
            if (error != null)
            {
                Console.WriteLine($"Tcp ping pong : client connection failed, error {error}.");
                tcp.CloseHandle(OnClose);
            }
            else
            {
                tcp.OnRead(this.OnAccept, OnError);

                // Sending the first ping
                tcp.QueueWriteStream(this.dataBuffer, OnWriteCompleted);
            }
        }

        static void OnWriteCompleted(StreamHandle stream, Exception error)
        {
            if (error == null)
            {
                return;
            }

            Console.WriteLine($"Tcp ping pong : failed, error {error}.");
            stream.CloseHandle(OnClose);
        }

        void OnAccept(StreamHandle stream, ReadableBuffer data)
        {
            if (data.Count == 0)
            {
                return;
            }

            string message = data.ReadString(data.Count, Encoding.UTF8);
            foreach (char token in message)
            {
                if (token == SplitToken)
                {
                    this.state = 0;
                }
                else
                {
                    if (token != PingMessage[this.state])
                    {
                        Console.WriteLine($"Tcp ping pong : failed, wrong message token received {token}.");
                        stream.Dispose();
                        return;
                    }

                    this.state++;
                }

                if (this.state == 0)
                {
                    this.pongs++;
                    long duration = this.loop.Now - this.startTime;

                    if (duration > DurationInMilliseconds)
                    {
                        stream.CloseHandle(OnClose);
                        this.server.CloseServer();
                    }
                    else
                    {
                        stream.QueueWriteStream(this.dataBuffer, OnWriteCompleted);
                    }
                }
            }
        }

        static void OnError(StreamHandle stream, Exception exception) =>
            Console.WriteLine($"Tcp ping pong read error {exception}");

        static void OnClose(ScheduleHandle handle) => handle.Dispose();

        public void Dispose()
        {
            this.dataBuffer.Dispose();
            this.server = null;
            this.loop = null;
        }
    }
}
