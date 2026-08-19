using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Numerics;
using System.Text;

using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Net.Http;

namespace SnowballServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            GameServer server = new GameServer();

            await server.StartAsync();
        }
    }

    public class GameServer
    {
        public async Task StartAsync()
        {
            Console.WriteLine("Snowball Server Start");

            NetworkServer networkServer = new NetworkServer();
            networkServer.Connect();

            await Task.Delay(Timeout.Infinite);
        }
    }



    class SnowItemState
    {
        public int Id;
        public Vector3 Position;
        public bool IsActive;
    }

    enum TcpPacketType : byte
    {
        Chat = 0x01,
        PlayerJoin = 0x02,
        AssignClientId = 0x03,
        PlayerLeave = 0x04,

        SnowItemRequest = 0x05,
        SnowItemResult = 0x06,
        SnowItemRespawn = 0x07,
        SnowballThrowRequest = 0x08
    }

    enum UdpPacketType : byte
    {
        Position = 0x02
    }

    public class NetworkServer
    {
        private TcpListener tcpServer;
        private UdpClient udpServer;
        private bool isRunning = false;
        private ConcurrentDictionary<int, TcpClient> tcpClients = new ConcurrentDictionary<int, TcpClient>();
        private ConcurrentDictionary<int, IPEndPoint> udpClients = new ConcurrentDictionary<int, IPEndPoint>();
        private ConcurrentDictionary<string, int> tokens = new ConcurrentDictionary<string, int>();
        private ConcurrentDictionary<int, Vector3> clientPositions = new ConcurrentDictionary<int, Vector3>();
        private ConcurrentDictionary<int, float> clientYaws = new ConcurrentDictionary<int, float>();
        private ConcurrentDictionary<int, int> clientColor = new ConcurrentDictionary<int, int>();
        private ConcurrentDictionary<int, string> clientName = new ConcurrentDictionary<int, string>();
        private ConcurrentDictionary<int, int> lastPositionSequence = new ConcurrentDictionary<int, int>();

        private Dictionary<int, SnowItemState> snowItems = new Dictionary<int, SnowItemState>();

        private ConcurrentDictionary<int, int> snowballCounts = new ConcurrentDictionary<int, int>();

        private int nextClientId = 0;

        public void Connect()
        {
            // TCP 시작
            StartTcpServer(9050);
            // UDP 시작
            StartUdpServer(9051);

            InitializeSnowItems();
        }

        void StartTcpServer(int port)
        {
            tcpServer = new TcpListener(IPAddress.Any, port);
            tcpServer.Start();
            isRunning = true;
            Console.WriteLine("TCP 서버가 시작되었습니다. 포트: " + port);

            _ = ListenForTcpClients();
        }

        void StartUdpServer(int port)
        {
            udpServer = new UdpClient(port);
            isRunning = true;
            Console.WriteLine("UDP 서버가 시작되었습니다. 포트: " + port);

            //ThreadPool.QueueUserWorkItem(ListenForUdpRequests);
            _ = ListenForUdpRequests();
        }
        void InitializeSnowItems()
        {
            snowItems[0] = new SnowItemState
            {
                Id = 0,
                Position = new Vector3(28f, 3f, -14f),
                IsActive = true
            };

            snowItems[1] = new SnowItemState
            {
                Id = 1,
                Position = new Vector3(-5f, 3f, -6f),
                IsActive = true
            };

            snowItems[2] = new SnowItemState
            {
                Id = 2,
                Position = new Vector3(6f, 3f, -18f),
                IsActive = true
            };

            snowItems[3] = new SnowItemState
            {
                Id = 3,
                Position = new Vector3(8f, 3f, 7f),
                IsActive = true
            };
        }
        async Task ListenForTcpClients()
        {
            Console.WriteLine("TCP 서버 대기 중..");

            while (isRunning)
            {
                try
                {
                    TcpClient tcpClient = await tcpServer.AcceptTcpClientAsync();

                    int clientId = Interlocked.Increment(ref nextClientId);

                    string token = Guid.NewGuid().ToString("N");

                    tokens[token] = clientId;

                    Console.WriteLine($"클라이언트 {clientId} 연결");

                    SendClientInfo(tcpClient, clientId, token);

                    _ = HandleTcpClient(tcpClient, clientId);
                }
                catch (Exception e)
                {
                    Console.WriteLine("TCP 클라이언트 연결 수락 실패: " + e);
                }
            }
        }
        async Task ListenForUdpRequests()
        {
            while (isRunning)
            {
                try
                {
                    UdpReceiveResult result = await udpServer.ReceiveAsync();

                    byte[] buffer = result.Buffer;


                    if (buffer == null || buffer.Length < 2)
                    {
                        Console.WriteLine("잘못된 UDP 패킷");
                        continue;
                    }

                    byte packetType = buffer[0];
                    int messageLength = buffer[1];

                    if (buffer.Length != 2 + messageLength)
                    {
                        Console.WriteLine("UDP 패킷 길이 오류");
                        continue;
                    }

                    switch (packetType)
                    {
                        //위치 정보 처리
                        case (byte)UdpPacketType.Position:
                            HandlePositionUpdate(buffer, result.RemoteEndPoint);
                            break;
                        default:
                            Console.WriteLine("알 수 없는 패킷 타입:" + packetType);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("UDP 요청 수신 실패: " + e);
                }
            }
        }

        async Task HandleTcpClient(TcpClient tcpClient, int clientId)
        {
            NetworkStream stream = tcpClient.GetStream();
            byte[] readBuffer = new byte[1024];
            List<byte> receiveBuffer = new List<byte>();

            //수신 대기
            while (true)
            {
                int bytesRead = 0;

                try
                {
                    bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                }
                catch (Exception e)
                {
                    Console.WriteLine("클라이언트와 연결 끊김");
                    break;
                }

                if (bytesRead == 0) break;

                for (int i = 0; i < bytesRead; i++)
                {
                    receiveBuffer.Add(readBuffer[i]);
                }

                ProcessTcpPackets(
                    receiveBuffer,
                    tcpClient,
                    clientId
                );
            }

            DisconnectClient(clientId);
        }

        void DisconnectClient(int clientId)
        {
            if (tcpClients.TryRemove(clientId, out TcpClient tcpClient))
            {
                tcpClient.Close();
            }

            udpClients.TryRemove(clientId, out _);
            clientColor.TryRemove(clientId, out _);
            clientName.TryRemove(clientId, out _);
            clientPositions.TryRemove(clientId, out _);

            string tokenToRemove = null;

            foreach (var token in tokens)
            {
                if (token.Value == clientId)
                {
                    tokenToRemove = token.Key;
                    break;
                }
            }

            if (tokenToRemove != null)
            {
                tokens.TryRemove(tokenToRemove, out _);
            }

            BroadcastPlayerLeave(clientId);

            Console.WriteLine($"클라이언트 {clientId} 정리 완료");
        }

        void BroadcastPlayerLeave(int clientId)
        {
            byte[] data = Encoding.UTF8.GetBytes(clientId.ToString());

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.PlayerLeave;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream = client.Value.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }

        void ProcessTcpPackets(List<byte> receiveBuffer, TcpClient tcpClient, int clientId)
        {
            const int headerSize = 2;

            while (true)
            {
                // Header조차 아직 안 왔음
                if (receiveBuffer.Count < headerSize)
                    return;

                byte packetType = receiveBuffer[0];
                int payloadLength = receiveBuffer[1];

                int packetLength =
                    headerSize + payloadLength;

                // 완전한 Packet이 아직 안 왔음
                if (receiveBuffer.Count < packetLength)
                    return;

                // Packet 하나 추출
                byte[] packet =
                    receiveBuffer
                        .GetRange(0, packetLength)
                        .ToArray();

                // 사용한 데이터 제거
                receiveBuffer.RemoveRange(
                    0,
                    packetLength
                );

                HandleTcpPacket(
                    packetType,
                    packet,
                    tcpClient,
                    clientId
                );

                // while 반복
                // Buffer 안에 Packet이 더 있으면 계속 처리
            }
        }

        void HandleTcpPacket(byte packetType, byte[] packet, TcpClient tcpClient, int clientId)
        {
            switch (packetType)
            {
                case (byte)TcpPacketType.Chat:
                    HandleChatMessage(packet, clientId);
                    break;

                case (byte)TcpPacketType.PlayerJoin:
                    HandlePlayerJoin(packet, clientId, tcpClient);
                    break;

                case (byte)TcpPacketType.SnowItemRequest:
                    HandleSnowItemRequest(packet, clientId);
                    break;

                case (byte)TcpPacketType.SnowballThrowRequest:
                    HandleSnowballThrowRequest(clientId);
                    break;

                default:
                    Console.WriteLine("알 수 없는 TCP 패킷 타입: " + packetType);
                    break;
            }
        }


        void SendClientInfo(TcpClient client, int clientId, string token)
        {
            string payload = $"{clientId}:{token}";
            byte[] data = Encoding.UTF8.GetBytes(payload);

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.AssignClientId;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            NetworkStream stream = client.GetStream();
            stream.Write(packet, 0, packet.Length);
        }
        void HandlePlayerJoin(byte[] buffer, int clientId, TcpClient tcpClient)
        {
            int byteRead = buffer[1];

            string data = Encoding.UTF8.GetString(buffer, 2, byteRead);

            Console.WriteLine("유저 데이터 수신: " + data);

            string[] parts = data.Split(':', 2);

            if (parts.Length != 2)
            {
                Console.WriteLine("잘못된 PlayerJoin 패킷");
                return;
            }

            if (!int.TryParse(parts[0], out int color))
            {
                Console.WriteLine("잘못된 캐릭터 번호");
                return;
            }

            string name = parts[1];

            tcpClients[clientId] = tcpClient;
            clientColor[clientId] = color;
            clientName[clientId] = name;
            clientPositions[clientId] = Vector3.Zero;

            BroadcastPlayerInfo(clientId, color, name);
        }
        void HandleChatMessage(byte[] buffer, int clientId)
        {
            byte messageLength = buffer[1];

            //위치 데이터 수신
            string chatMessage = Encoding.UTF8.GetString(buffer, 2, messageLength);

            BroadcastMessage(clientId, chatMessage);
        }

        void BroadcastMessage(int senderId, string chatMessage)
        {
            string payload = $"{senderId}:{chatMessage}:";
            byte[] data = Encoding.UTF8.GetBytes(payload);

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.Chat;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (client.Key == senderId)
                    continue;

                if (!client.Value.Connected)
                    continue;

                NetworkStream stream = client.Value.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }

        void BroadcastPlayerInfo(int senderID, int color, string name)
        {
            string chat = $"{senderID}:{color}:{name}";
            byte[] data = Encoding.UTF8.GetBytes(chat);
            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.PlayerJoin;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)//새로운 클라이언트를 이미 접속한 모든 클라이언트에 전송
            {
                if (client.Value.Connected)
                {
                    if (client.Key != senderID)
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                }
            }

            foreach (var client in tcpClients)//이미 접속한 모든 클라이언트를 새로운 클라이언트에 전송
            {
                if (client.Value.Connected)
                {
                    if (client.Key != senderID)
                    {
                        data = Encoding.UTF8.GetBytes($"{client.Key}:{clientColor[client.Key]}:{clientName[client.Key]}");
                        packet = new byte[data.Length + 2];
                        packet[0] = (byte)TcpPacketType.PlayerJoin;
                        packet[1] = (byte)data.Length;
                        Array.Copy(data, 0, packet, 2, data.Length);

                        NetworkStream stream = tcpClients[senderID].GetStream();
                        stream.Write(packet, 0, packet.Length);
                        Console.WriteLine("서버 : 색, 이름 보냄");
                    }
                }
            }
        }


        void HandlePositionUpdate(byte[] buffer, IPEndPoint endPoint)
        {
            byte messageLength = buffer[1];

            //위치 데이터 수신
            string data = Encoding.UTF8.GetString(buffer, 2, messageLength);
            //Debug.Log("위치 데이터 수신: " + data);

            string[] parts = data.Split(':', 4);

            if (parts.Length != 4)
                return;

            string token = parts[0];

            if (!tokens.TryGetValue(token, out int clientId))
            {
                Console.WriteLine("유효하지 않은 UDP 토큰");
                return;
            }
            if (!int.TryParse(parts[1], out int sequence))
                return;

            if (lastPositionSequence.TryGetValue(clientId, out int lastSequence))
            {
                if (sequence <= lastSequence)
                    return;
            }
            lastPositionSequence[clientId] = sequence;

            string[] position = parts[2].Split(',');

            float x = float.Parse(position[0]);
            float y = float.Parse(position[1]);
            float z = float.Parse(position[2]);

            clientPositions[clientId] = new Vector3(x, y, z);

            float yaw = float.Parse(parts[3]);

            clientYaws[clientId] = yaw;

            if (!udpClients.TryGetValue(clientId, out IPEndPoint registeredEndPoint))
            {
                udpClients[clientId] = endPoint;
            }
            else if (!registeredEndPoint.Equals(endPoint))
            {
                Console.WriteLine(
                    $"UDP Endpoint 불일치: Client {clientId}"
                );
                return;
            }


            //다른 클라이언트에게 새로운 위치 전송
            BroadcastPositionToClients(clientId, sequence);
        }

        void BroadcastPositionToClients(int senderID, int sequence)
        {
            string position = 
                $"{senderID}:{sequence}:" +
                $"{clientPositions[senderID].X}," +
                $"{clientPositions[senderID].Y}," +
                $"{clientPositions[senderID].Z}:" +
                $"{clientYaws[senderID]}";
            //Debug.Log("데이터 취합: " + position + "그리고" + senderID);

            byte[] data = Encoding.UTF8.GetBytes(position);
            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)UdpPacketType.Position;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);


            foreach (var client in udpClients)
            {
                if (tcpClients[client.Key].Connected && client.Key != senderID)
                {
                    try
                    {
                        udpServer.Send(packet, packet.Length, udpClients[client.Key]);
                        //Debug.Log("위치 데이터 전송 완료: " + udpClients[client.Key]);
                        //Debug.Log("위치 데이터: " + packet[0] + " " + packet[1] + " " + packet[2]);
                    }
                    catch (Exception e)
                    {
                        //Debug.Log("클라이언트에게 데이터 보내기 실패: " + e);
                    }

                }
            }
        }

        void HandleSnowItemRequest(byte[] buffer, int clientId)
        {
            int messageLength = buffer[1];

            string data = Encoding.UTF8.GetString(buffer, 2, messageLength);

            if (!int.TryParse(data, out int itemId))
                return;

            if (!snowItems.TryGetValue(itemId, out SnowItemState item))
                return;

            if (!item.IsActive)
                return;

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
                return;

            float distance =
                Vector3.Distance(playerPosition, item.Position);

            if (distance > 2f)
                return;

            item.IsActive = false;

            int newCount = snowballCounts.AddOrUpdate(
                clientId,
                1,
                (_, current) => current + 1);

            Console.WriteLine(
        $"Client {clientId} SnowItem {itemId} 획득 / Snowball {newCount}"
    );

            BroadcastSnowItemResult(
                itemId,
                clientId,
                newCount);

            _ = RespawnSnowItem(itemId);
        }

        void BroadcastSnowItemResult(
            int itemId,
            int winnerClientId,
            int snowballCount)
        {
            string payload =
                $"{itemId}:{winnerClientId}:{snowballCount}";

            byte[] data = Encoding.UTF8.GetBytes(payload);

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.SnowItemResult;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream = client.Value.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }

        async Task RespawnSnowItem(int itemId)
        {
            await Task.Delay(5000);

            if (!snowItems.TryGetValue(itemId, out SnowItemState item))
                return;

            item.IsActive = true;

            BroadcastSnowItemRespawn(itemId);
        }

        void BroadcastSnowItemRespawn(int itemId)
        {
            byte[] data =
                Encoding.UTF8.GetBytes(itemId.ToString());

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.SnowItemRespawn;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream = client.Value.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }

        void HandleSnowballThrowRequest(int clientId)
        {
            if (!snowballCounts.TryGetValue(
                clientId,
                out int snowballCount))
            {
                return;
            }

            if (snowballCount <= 0)
                return;

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
            {
                return;
            }

            if (!clientYaws.TryGetValue(
                clientId,
                out float yaw))
            {
                return;
            }

            snowballCounts[clientId]--;

            float rad = yaw * MathF.PI / 180f;

            Vector3 direction = new Vector3(
                MathF.Sin(rad),
                0f,
                MathF.Cos(rad)
            );

            Console.WriteLine(
                $"Client {clientId} 눈덩이 투척 / " +
                $"남은 개수: {snowballCounts[clientId]} / " +
                $"방향: {direction}"
            );
        }
        void OnApplicationQuit()
        {
            foreach (var client in tcpClients)
            {
                if (client.Value.Connected)
                {
                    client.Value.Close();  // 각 클라이언트의 연결을 종료
                }
            }
            if (udpServer != null)
                udpServer.Close();

            if (tcpServer != null)
                tcpServer.Stop();  // TCP 서버 종료

            isRunning = false;
            Console.WriteLine("서버 종료");
        }
    }
}

