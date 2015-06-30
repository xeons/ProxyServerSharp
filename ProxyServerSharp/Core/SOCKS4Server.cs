﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace ProxyServerSharp
{
    class ConnectionInfo
    {
        public Socket LocalSocket;
        public Thread LocalThread;
        public Socket RemoteSocket;
        public Thread RemoteThread;
    }

    public delegate void ConnectEventHandler(object sender, IPEndPoint iep);
    public delegate void ConnectionLogHandler(object sender, int code, string message);

    class SOCKS4Server
    {
        private Socket _serverSocket;
        private int _port;
        private int _transferUnitSize;
        private Thread _acceptThread;
        private List<ConnectionInfo> _connections =
            new List<ConnectionInfo>();

        public event ConnectEventHandler LocalConnect;
        public event ConnectEventHandler RemoteConnect;

        public SOCKS4Server(int port, int transferUnitSize) 
        { 
            _port = port;
            _transferUnitSize = transferUnitSize;
        }

        public void Start()
        {
            SetupServerSocket();

            _acceptThread = new Thread(AcceptConnections);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
        }

        private void SetupServerSocket()
        {
            IPEndPoint myEndpoint = new IPEndPoint(IPAddress.Loopback, 
                _port);

            // Create the socket, bind it, and start listening
            _serverSocket = new Socket(myEndpoint.Address.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);
            _serverSocket.Bind(myEndpoint);
            _serverSocket.Listen((int)SocketOptionName.MaxConnections);
        }

        private void AcceptConnections()
        {
            while (true)
            {
                // Accept a connection
                ConnectionInfo connection = new ConnectionInfo();

                Socket socket = _serverSocket.Accept();

                connection.LocalSocket = socket;
                connection.RemoteSocket = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp);

                // Create the thread for the receives.
                connection.LocalThread = new Thread(ProcessLocalConnection);
                connection.LocalThread.IsBackground = true;
                connection.LocalThread.Start(connection);

                if (LocalConnect != null)
                    LocalConnect(this, (IPEndPoint)socket.RemoteEndPoint);

                // Store the socket
                lock (_connections) _connections.Add(connection);
            }
        }

        private void ProcessLocalConnection(object state)
        {
            ConnectionInfo connection = (ConnectionInfo)state;
            int bytesRead = 0;

            byte[] buffer = new byte[_transferUnitSize];
            try
            {
                // we are setting up the socks!
                bytesRead = connection.LocalSocket.Receive(buffer);

                Console.WriteLine("ProcessLocalConnection::Receive bytesRead={0}", bytesRead);
                for (int i = 0; i < bytesRead; i++)
                    Console.Write("{0:X2} ", buffer[i]);
                Console.Write("\n");

                if (bytesRead > 0)
                {
                    if (buffer[0] == 0x04 && buffer[1] == 0x01)
                    {
                        int remotePort = buffer[2] << 8 | buffer[3];
                        byte[] ipAddressBuffer = new byte[4];
                        Buffer.BlockCopy(buffer, 4, ipAddressBuffer, 0, 4);
                        
                        IPEndPoint remoteEndPoint = new IPEndPoint(new IPAddress(ipAddressBuffer), remotePort);

                        connection.RemoteSocket.Connect(remoteEndPoint);
                        if (connection.RemoteSocket.Connected)
                        {
                            Console.WriteLine("Connected to remote!");

                            if (RemoteConnect != null)
                                RemoteConnect(this, remoteEndPoint);

                            byte[] socksResponse = new byte[] {
                                0x00, 0x5a,
                                buffer[2], buffer[3], // port
                                buffer[4], buffer[5], buffer[6], buffer[7] // IP
                            };
                            connection.LocalSocket.Send(socksResponse);

                            // Create the thread for the receives.
                            connection.RemoteThread = new Thread(ProcessRemoteConnection);
                            connection.RemoteThread.IsBackground = true;
                            connection.RemoteThread.Start(connection);
                        } 
                        else 
                        {
                            Console.WriteLine("Connection failed.");
                            byte[] socksResponse = new byte[] {
                                0x04, 
                                0x5b,
                                buffer[2], buffer[3], // port
                                buffer[4], buffer[5], buffer[6], buffer[7] // IP
                            };
                            connection.LocalSocket.Send(socksResponse);
                            return;

                        }
                    }
                }
                else if (bytesRead == 0) return;

                // start receiving actual data
                while (true)
                {
                    bytesRead = connection.LocalSocket.Receive(buffer);
                    if (bytesRead == 0) {
                        Console.WriteLine("Local connection closed!");
                        break;
                    } else {
                        connection.RemoteSocket.Send(buffer, bytesRead, SocketFlags.None);
                    }
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception: " + exc);
            }
            finally
            {
                Console.WriteLine("ProcessLocalConnection Cleaning up...");
                connection.LocalSocket.Close();
                connection.RemoteSocket.Close();
                lock (_connections) _connections.Remove(connection);
            }
        }

        private void ProcessRemoteConnection(object state)
        {
            ConnectionInfo connection = (ConnectionInfo)state;
            int bytesRead = 0;

            byte[] buffer = new byte[_transferUnitSize];
            try
            {
                // start receiving actual data
                while (true)
                {
                    bytesRead = connection.RemoteSocket.Receive(buffer);
                    if (bytesRead == 0) {
                        Console.WriteLine("Remote connection closed!");
                        break;
                    } else {
                        connection.LocalSocket.Send(buffer, bytesRead, SocketFlags.None);
                    }
                }
            }
            catch (SocketException exc)
            {
                Console.WriteLine("Socket exception: " + exc.SocketErrorCode);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception: " + exc);
            }
            finally
            {
                Console.WriteLine("ProcessRemoteConnection Cleaning up...");
                connection.LocalSocket.Close();
                connection.RemoteSocket.Close();
                lock (_connections) 
                    _connections.Remove(connection);
            }
        }
    }

}
