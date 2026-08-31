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
using static System.Collections.Specialized.BitVector32;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

            _ = networkServer.StartGameLoop();

            await Task.Delay(Timeout.Infinite);
        }
    }

    class BridgeState
    {
        public int OwnerClientId;
        public bool IsPurchased;
        public Vector3 PurchasePosition;
    }

    class StorageState
    {
        public int OwnerClientId;
        public int SnowballCount;
    }
    class SnowFieldState
    {
        public int OwnerClientId;

        public Vector3 CurrentSnowPosition;

        public int MinX;
        public int MaxX;

        public int MinZ;
        public int MaxZ;
    }
    class DroppedSnowballState
    {
        public int Id;
        public Vector3 Position;
        public int Count;
    }

    class SnowballState
    {
        public int Id;
        public int OwnerId;

        public Vector3 Position;
        public Vector3 SpawnPosition;
        public Vector3 Direction;

        public float Speed;
        public float MaxDistance;
    }

    enum GameState
    {
        Waiting,
        Playing,
        Finished
    }

    enum StorageAction : byte
    {
        DepositAll = 0,
        DepositHalf = 1,
        WithdrawAll = 2,
        WithdrawHalf = 3
    }

    enum TcpPacketType : byte
    {
        Chat = 0x01,
        PlayerJoin = 0x02,
        AssignClientId = 0x03,
        PlayerLeave = 0x04,

        SnowItemRequest = 0x05,
        SnowItemResult = 0x06,
        //SnowItemRespawn = 0x07,
        SnowballThrowRequest = 0x08,
        SnowballSpawn = 0x09,
        SnowballDespawn = 0x0A,

        PlayerHit = 0x0B,
        PlayerRespawn = 0x0C,

        DroppedSnowballs = 0x0D,
        DroppedSnowItemRequest = 0x0E,
        DroppedSnowItemResult = 0x0F,

        SnowFieldJoin = 0x10,

        GunPurchaseRequest = 0x11,
        GunPurchaseResult = 0x12,
        GunRemoved = 0x13,

        PlayerKnockback = 0x14,

        PlayerStorageJoin = 0x15,
        StorageRequest = 0x16,
        StorageResult = 0x17,

        GameStartRequest = 0x18,
        GameStart = 0x19,

        CentralSnowball = 0x20,
        CentralSnowballResult = 0x21,

        BridgePurchaseRequest = 0x22,
        BridgePurchaseResult = 0x23,
        BridgeOtherPlayerJoin = 0x24,
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
        private ConcurrentDictionary<int, int> playerHps = new ConcurrentDictionary<int, int>();
        private ConcurrentDictionary<int, bool> playerDead = new ConcurrentDictionary<int, bool>();
        private ConcurrentDictionary<int, Vector3> playerSpawnPositions = new ConcurrentDictionary<int, Vector3>();

        private ConcurrentDictionary<int, int> snowballCounts = new ConcurrentDictionary<int, int>();
        private ConcurrentDictionary<int, SnowballState> snowballs = new ConcurrentDictionary<int, SnowballState>();
        private ConcurrentDictionary<int, SnowFieldState> snowFields = new ConcurrentDictionary<int, SnowFieldState>();
        private ConcurrentDictionary<int, DroppedSnowballState> droppedSnowballs = new ConcurrentDictionary<int, DroppedSnowballState>();

        private ConcurrentDictionary<int, int> gunLevels = new ConcurrentDictionary<int, int>();
        private ConcurrentDictionary<int, int> gunPrices = new ConcurrentDictionary<int, int>();

        private Vector3 gunShopPosition = new Vector3(6, 0, -3);

        private ConcurrentDictionary<int, StorageState> storages = new ConcurrentDictionary<int, StorageState>();

        private Vector3 storagePosition = new Vector3(4, 0, -3);

        private ConcurrentDictionary<int, BridgeState> bridges = new ConcurrentDictionary<int, BridgeState>();

        private Vector3 bridgePosition = new Vector3(10, 3, 10);
        private const int BridgePrice = 5;

        private int nextSnowballId = 0;
        private int nextDropSnowballId = 0;
        private int nextClientId = 0;

        private readonly Random random = new Random();

        private GameState gameState = GameState.Waiting;

        private int centralSnowballCount = 0;
        private float centralSnowballTimer = 0f;
        private const int CentralSnowballMaxCount = 10;

        private const float CentralSnowballInterval = 5f;

        private Vector3 centralSnowballPosition = new Vector3(11, 0, 91.8f);

        public async Task StartGameLoop()
        {
            const float tickRate = 20f;
            const float deltaTime = 1f / tickRate;

            int delayMs = (int)(1000f / tickRate);

            while (isRunning)
            {
                UpdateSnowballs(deltaTime);

                if (gameState == GameState.Playing)
                {
                    UpdateCentralSnowball(deltaTime);
                }

                await Task.Delay(delayMs);
            }
        }

        public void Connect()
        {
            // TCP 시작
            StartTcpServer(9050);
            // UDP 시작
            StartUdpServer(9051);

            InitializeSnowField();
            InitializeGunPrice();
            InitializeStorages();
            InitializeBridges();
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

        void InitializeBridges()
        {
            for (int i = 0; i < 4; i++)
            {
                bridges[i] = new BridgeState
                {
                    OwnerClientId = -1,
                    IsPurchased = false
                };
            }
        }

        void InitializeStorages()
        {
            for (int i = 0; i < 4; i++)
            {
                storages[i] = new StorageState
                {
                    OwnerClientId = -1,
                    SnowballCount = 0
                };
            }
        }

        void InitializeGunPrice()
        {
            gunPrices[0] = 3;
            gunPrices[1] = 10;
            gunPrices[2] = 30;
        }

        void InitializeSnowField()
        {
            snowFields[0] = new SnowFieldState
            {
                OwnerClientId = -1,

                MinX = 1,
                MaxX = 5,

                MinZ = 1,
                MaxZ = 5,
            };
            snowFields[1] = new SnowFieldState
            {
                OwnerClientId = -1,

                MinX = 6,
                MaxX = 10,

                MinZ = 6,
                MaxZ = 10,
            };
            snowFields[2] = new SnowFieldState
            {
                OwnerClientId = -1,

                MinX = -5,
                MaxX = -1,

                MinZ = -5,
                MaxZ = -1,
            };
            snowFields[3] = new SnowFieldState
            {
                OwnerClientId = -1,

                MinX = -10,
                MaxX = -6,

                MinZ = -10,
                MaxZ = -6,
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
            clientYaws.TryRemove(clientId, out _);
            lastPositionSequence.TryRemove(clientId, out _);
            playerHps.TryRemove(clientId, out _);
            playerDead.TryRemove(clientId, out _);
            snowballCounts.TryRemove(clientId, out _);
            gunLevels.TryRemove(clientId, out _);


            foreach (var snowField in snowFields)
            {
                if (snowField.Value.OwnerClientId == clientId)
                {
                    snowField.Value.OwnerClientId = -1;
                }
            }

            foreach (var storage in storages)
            {
                if (storage.Value.OwnerClientId == clientId)
                {
                    storage.Value.OwnerClientId = -1;
                    storage.Value.SnowballCount = 0;
                }
            }

            foreach (var bridge in bridges)
            {
                if (bridge.Value.OwnerClientId == clientId)
                {
                    bridge.Value.OwnerClientId = -1;
                    bridge.Value.IsPurchased = false;
                }
            }

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

                case (byte)TcpPacketType.DroppedSnowItemRequest:
                    HandleDroppedSnowItemRequest(packet, clientId);
                    break;

                case (byte)TcpPacketType.GunPurchaseRequest:
                    HandleGunPurchaseRequest(packet, clientId);
                    break;

                case (byte)TcpPacketType.StorageRequest:
                    HandleStorageRequest(packet, clientId);
                    break;

                case (byte)TcpPacketType.GameStartRequest:
                    HandleGameStartRequest(packet, clientId);
                    break;

                case (byte)TcpPacketType.CentralSnowball:
                    HandleCentralSnowballRequest(packet, clientId);
                    break;

                case (byte)TcpPacketType.BridgePurchaseRequest:
                    HandleBridgePurchaseRequest(packet, clientId);
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
            clientPositions[clientId] = new Vector3(0, 5, 0);

            playerHps[clientId] = 5;
            playerDead[clientId] = false;
            gunLevels[clientId] = 0;
            snowballCounts[clientId] = 0;

            playerSpawnPositions[clientId] = clientPositions[clientId];

            foreach (var snowField in snowFields)
            {
                if (snowField.Value.OwnerClientId == -1)
                {
                    snowField.Value.OwnerClientId = clientId;

                    int posX = random.Next(snowField.Value.MinX, snowField.Value.MaxX);
                    int posZ = random.Next(snowField.Value.MinZ, snowField.Value.MaxZ);

                    snowField.Value.CurrentSnowPosition = new Vector3(posX, 3, posZ);

                    BroadcastSnowFieldInfo(clientId, snowField.Value.CurrentSnowPosition);
                    break;
                }
            }

            foreach (var storage in storages)
            {
                if (storage.Value.OwnerClientId == -1)
                {
                    storage.Value.OwnerClientId = clientId;

                    storage.Value.SnowballCount = 0;

                    bridges[storage.Key].OwnerClientId = clientId;
                    bridges[storage.Key].IsPurchased = false;

                    BroadcastStorageInfo(clientId, storage.Key);
                    break;
                }
            }

            foreach (var field in snowFields.Values)
            {
                if (field.OwnerClientId == -1 || field.OwnerClientId == clientId)
                    continue;

                SendSnowFieldToClient(
                    tcpClient,
                    field.OwnerClientId,
                    field.CurrentSnowPosition
                );
            }

            foreach (var bridge in bridges)
            {
                if (bridge.Value.OwnerClientId == -1 || bridge.Value.OwnerClientId == clientId)
                    continue;

                SendBridgeToClient(
                    tcpClient,
                    bridge.Value.OwnerClientId,
                    bridge.Key
                );
            }



            BroadcastPlayerInfo(clientId, color, name);

            foreach (var gunLevel in gunLevels)
            {
                if (gunLevel.Value == 0)
                    continue;

                SendGunInfoToClient(
                    tcpClient,
                    gunLevel.Key
                );
            }
        }
        void SendGunInfoToClient(TcpClient tcpClient, int onwerId)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{onwerId}" +
                $"{gunLevels[onwerId]}" +
                $"{snowballCounts[onwerId]}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.GunPurchaseResult;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            if (tcpClient.Connected)
            {
                NetworkStream stream = tcpClient.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }
        void SendBridgeToClient(TcpClient tcpClient, int onwerId, int bridgeKey)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{onwerId}:" +
                $"{bridgeKey}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.BridgeOtherPlayerJoin;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);

            if (tcpClient.Connected)
            {
                NetworkStream stream = tcpClient.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }
        void SendSnowFieldToClient(TcpClient tcpClient, int onwerId, Vector3 pos)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{onwerId}:" +
                $"{pos.X}," +
                $"{pos.Y}," +
                $"{pos.Z}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.SnowFieldJoin;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);

            if (tcpClient.Connected)
            {
                NetworkStream stream = tcpClient.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }

        void BroadcastStorageInfo(int ownerId, int storageKey)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{ownerId}:" +
                $"{storageKey}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.PlayerStorageJoin;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)//새로운 클라이언트를 이미 접속한 모든 클라이언트에 전송
            {
                if (client.Value.Connected)
                {
                    NetworkStream stream = client.Value.GetStream();
                    stream.Write(packet, 0, packet.Length);
                }
            }
        }
        void BroadcastSnowFieldInfo(int senderID, Vector3 pos)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{senderID}:" +
                $"{pos.X}," +
                $"{pos.Y}," +
                $"{pos.Z}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.SnowFieldJoin;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)//새로운 클라이언트를 이미 접속한 모든 클라이언트에 전송
            {
                if (client.Value.Connected)
                {
                    NetworkStream stream = client.Value.GetStream();
                    stream.Write(packet, 0, packet.Length);
                }
            }
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

            string[] parts = data.Split(':', 5);

            if (parts.Length != 5)
                return;

            string token = parts[0];

            if (!tokens.TryGetValue(token, out int clientId))
            {
                Console.WriteLine("유효하지 않은 UDP 토큰");
                return;
            }
            if (!int.TryParse(parts[1], out int sequence))
                return;

            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

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

            bool isMoving = parts[4] == "1";

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
            BroadcastPositionToClients(clientId, sequence, isMoving);
        }

        void BroadcastPositionToClients(int senderID, int sequence, bool isMoving)
        {
            string position =
                $"{senderID}:{sequence}:" +
                $"{clientPositions[senderID].X}," +
                $"{clientPositions[senderID].Y}," +
                $"{clientPositions[senderID].Z}:" +
                $"{clientYaws[senderID]}:" +
                $"{(isMoving ? "1" : "0")}";
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

        void HandleGameStartRequest(byte[] buffer, int clientId)
        {
            if (gameState != GameState.Waiting)
                return;

            StartGame();
        }

        void StartGame()
        {
            gameState = GameState.Playing;

            Console.WriteLine("Game Start");

            centralSnowballCount = 0;
            centralSnowballTimer = 0f;

            foreach (var client in tcpClients)
            {

                if (!client.Value.Connected)
                {
                    continue;
                }

                int storageNum = 0;
                foreach (var storage in storages)
                {

                    if (storage.Value.OwnerClientId == client.Key)
                    {

                        Console.WriteLine($"hp: {playerHps[client.Key]}");
                        Console.WriteLine($"snowballCount: {snowballCounts[client.Key]}");
                        Console.WriteLine($"gunLevel: {gunLevels[client.Key]}");
                        Console.WriteLine($"storage: {storage.Value.SnowballCount}");
                        Console.WriteLine($"playerDead: {playerDead[client.Key]}");


                        storage.Value.SnowballCount = 0;
                        storageNum = storage.Key;

                        bridges[storage.Key].IsPurchased = false;
                    }
                }

                if (playerHps.TryGetValue(client.Key, out _))
                {
                    playerHps[client.Key] = 5;
                }

                if (snowballCounts.TryGetValue(client.Key, out _))
                {
                    snowballCounts[client.Key] = 0;
                }

                if (gunLevels.TryGetValue(client.Key, out _))
                {
                    gunLevels[client.Key] = 0;
                }

                if (playerDead.TryGetValue(client.Key, out _))
                {
                    playerDead[client.Key] = false;
                }

                droppedSnowballs.Clear();


                Console.WriteLine($"hp: {playerHps[client.Key]}" +
                    $"snowballCount: {snowballCounts[client.Key]}" +
                    $"gunLevel: {gunLevels[client.Key]}" +
                    $"storage: {storages[storageNum].SnowballCount}" +
                    $"playerDead: {playerDead[client.Key]}");

            }
            BroadcastGameStart();
        }

        void HandleBridgePurchaseRequest(byte[] buffer, int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

            if (gameState != GameState.Playing)
                return;

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
            {
                return;
            }

            float dx = bridgePosition.X - playerPosition.X;

            float dz = bridgePosition.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 2f)
            {
                return;

            }


            if (!snowballCounts.TryGetValue(
                clientId,
                out int snowballCount))
                return;


            if (snowballCount < BridgePrice)
                return;

            foreach (var bridge in bridges)
            {
                if (bridge.Value.OwnerClientId == clientId)
                {
                    if (bridge.Value.IsPurchased) return;

                    bridge.Value.IsPurchased = true;
                    snowballCounts[clientId] -= BridgePrice;

                    BroadcastBridgePurchaseResult(clientId);

                    return;
                }
            }
        }

        void HandleCentralSnowballRequest(byte[] buffer, int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
            {
                return;
            }

            float dx = centralSnowballPosition.X - playerPosition.X;

            float dz = centralSnowballPosition.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 10f)
            {
                return;

            }


            if (!snowballCounts.TryGetValue(
                clientId,
                out int snowballCount))
                return;


            if (centralSnowballCount <= 0)
                return;


            if (gameState != GameState.Playing)
                return;


            snowballCounts[clientId] += centralSnowballCount;
            centralSnowballCount = 0;
            centralSnowballTimer = 0f;

            BroadcastCentralSnowballResult(clientId);
        }

        void HandleStorageRequest(byte[] buffer, int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
                return;

            float dx = storagePosition.X - playerPosition.X;

            float dz = storagePosition.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 2f)
                return;

            if (!snowballCounts.TryGetValue(
                clientId,
                out int snowballCount))
                return;

            int messageLength = buffer[1];

            StorageAction action = (StorageAction)buffer[2];

            foreach (var storage in storages)
            {
                if (storage.Value.OwnerClientId == clientId)
                {
                    switch (action)
                    {
                        case StorageAction.DepositAll:
                            if (snowballCounts[clientId] <= 0) return;

                            storage.Value.SnowballCount += snowballCounts[clientId];
                            snowballCounts[clientId] = 0;

                            break;

                        case StorageAction.DepositHalf:
                            if (snowballCounts[clientId] <= 0) return;

                            int amountDeposit = snowballCounts[clientId] / 2;

                            storage.Value.SnowballCount += amountDeposit;
                            snowballCounts[clientId] -= amountDeposit;

                            break;

                        case StorageAction.WithdrawAll:
                            if (storage.Value.SnowballCount <= 0) return;

                            snowballCounts[clientId] += storage.Value.SnowballCount;
                            storage.Value.SnowballCount = 0;

                            break;

                        case StorageAction.WithdrawHalf:
                            if (storage.Value.SnowballCount <= 0) return;

                            int amountWithdraw = storage.Value.SnowballCount / 2;

                            snowballCounts[clientId] += amountWithdraw;
                            storage.Value.SnowballCount -= amountWithdraw;

                            break;
                    }

                    Console.WriteLine("보관: " + storage.Value.SnowballCount);
                    Console.WriteLine("소유: " + snowballCounts[clientId]);

                    SendStorageActionResult(clientId, storage.Key, storage.Value.SnowballCount, snowballCounts[clientId]);

                    return;
                }
            }
        }

        void HandleGunPurchaseRequest(byte[] buffer, int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
                return;

            float dx = gunShopPosition.X - playerPosition.X;

            float dz = gunShopPosition.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 2f)
                return;

            if (!snowballCounts.TryGetValue(
                clientId,
                out int snowballCount))
                return;

            if (!gunLevels.TryGetValue(clientId, out int gunLevel))
                return;

            if (gunLevel > 2)
                return;

            if (!gunPrices.TryGetValue(gunLevel, out int gunPrice))
                return;

            if (snowballCount < gunPrice)
                return;



            snowballCounts[clientId] -= gunPrice;
            gunLevels[clientId] = gunLevel + 1;

            BroadcastGunPurchaseResult(clientId);
        }

        void SendStorageActionResult(int clientId, int storageKey, int storageSnowballCount, int playerSnowballCount)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{storageKey}:" +
                $"{storageSnowballCount}:" +
                $"{playerSnowballCount}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.StorageResult;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                if (client.Key != clientId)
                    continue;

                NetworkStream stream = client.Value.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }

        void HandleDroppedSnowItemRequest(byte[] buffer, int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

            int messageLength = buffer[1];

            string data = Encoding.UTF8.GetString(buffer, 2, messageLength);

            if (!int.TryParse(data, out int itemId))
            {
                return;
            }

            if (!droppedSnowballs.TryGetValue(itemId, out DroppedSnowballState item))
                return;

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
                return;

            float dx = item.Position.X - playerPosition.X;

            float dz = item.Position.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 2.5f)
                return;

            if (!droppedSnowballs.TryRemove(itemId, out item))
                return;

            int newCount = snowballCounts.AddOrUpdate(
                clientId,
                item.Count,
                (_, current) => current + item.Count);

            Console.WriteLine(
                $"Client {clientId} DroppedSnowItem {itemId}, {item.Count} 획득 / Snowball {newCount}"
            );

            BroadcastDroppedSnowItemResult(
                itemId,
                clientId,
                newCount);
        }

        void BroadcastGameStart()
        {
            byte[] packet = new byte[2];

            packet[0] = (byte)TcpPacketType.GameStart;
            packet[1] = 0;

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream = client.Value.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
        }

        void BroadcastBridgePurchaseResult(int clientId)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{clientId}:" +
                $"{snowballCounts[clientId]}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.BridgePurchaseResult;
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

        void BroadcastCentralSnowballResult(int clientId)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{clientId}:" +
                $"{snowballCounts[clientId]}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.CentralSnowballResult;
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

        void BroadcastGunPurchaseResult(int clientId)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{clientId}:" +
                $"{gunLevels[clientId]}:" +
                $"{snowballCounts[clientId]}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.GunPurchaseResult;
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
        void BroadcastDroppedSnowItemResult(int itemId, int clientId, int snowballCount)
        {
            string payload =
                $"{itemId}:{clientId}:{snowballCount}";

            byte[] data = Encoding.UTF8.GetBytes(payload);

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.DroppedSnowItemResult;
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

        void HandleSnowItemRequest(byte[] buffer, int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

            SnowFieldState playerField = null;

            foreach (var field in snowFields.Values)
            {
                if (field.OwnerClientId == clientId)
                {
                    playerField = field;
                    break;
                }
            }

            if (playerField == null)
                return;

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
                return;

            float dx = playerField.CurrentSnowPosition.X - playerPosition.X;

            float dz = playerField.CurrentSnowPosition.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 2.5f)
                return;

            int newCount = snowballCounts.AddOrUpdate(
                clientId,
                1,
                (_, current) => current + 1);

            int posX = random.Next(playerField.MinX, playerField.MaxX);

            int posZ = random.Next(playerField.MinZ, playerField.MaxZ);

            playerField.CurrentSnowPosition = new Vector3(posX, 3f, posZ);


            Console.WriteLine(
        $"Client {clientId} SnowFieldItem 획득 / Snowball {newCount}"
    );

            BroadcastSnowItemResult(
                clientId,
                newCount,
                playerField.CurrentSnowPosition);
        }

        void BroadcastSnowItemResult(
            int winnerClientId,
            int snowballCount,
            Vector3 pos)
        {
            string payload =
                $"{winnerClientId}:" +
                $"{snowballCount}:" +
                $"{pos.X}," +
                $"{pos.Y}," +
                $"{pos.Z}";

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

        void HandleSnowballThrowRequest(int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

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

            if (!gunLevels.TryGetValue(clientId, out int gunLevel))
                return;

            if (gunLevel == 0)
                return;

            snowballCounts[clientId]--;

            float rad = yaw * MathF.PI / 180f;

            Vector3 direction = new Vector3(
                MathF.Sin(rad),
                0f,
                MathF.Cos(rad)
            );


            int snowballId = Interlocked.Increment(ref nextSnowballId);

            Vector3 spawnPosition = playerPosition + new Vector3(0f, 1f, 0f) + direction * 1.0f;

            SnowballState snowball = new SnowballState
            {
                Id = snowballId,
                OwnerId = clientId,
                SpawnPosition = spawnPosition,
                Position = spawnPosition,
                Direction = direction,
                Speed = 10f * gunLevel,
                MaxDistance = 30f,
            };

            snowballs[snowballId] = snowball;

            BroadcastSnowballSpawn(snowball);

            Console.WriteLine(
                $"Snowball {snowballId} 생성 / " +
                $"Owner {clientId} / " +
                $"Position {spawnPosition}"
            );
        }

        void BroadcastSnowballSpawn(SnowballState snowball)
        {
            string payload =
                $"{snowball.Id}:"
                + $"{snowball.OwnerId}:"
                + $"{snowball.Position.X},"
                + $"{snowball.Position.Y},"
                + $"{snowball.Position.Z}:"
                + $"{snowball.Direction.X},"
                + $"{snowball.Direction.Y},"
                + $"{snowball.Direction.Z}:"
                + $"{snowball.Speed}";

            byte[] data = Encoding.UTF8.GetBytes(payload);

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.SnowballSpawn;

            packet[1] = (byte)data.Length;

            Array.Copy(
                data,
                0,
                packet,
                2,
                data.Length
            );

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream =
                    client.Value.GetStream();

                stream.Write(
                    packet,
                    0,
                    packet.Length
                );
            }
        }

        void UpdateSnowballs(float deltaTime)
        {
            List<int> removeSnowballIds = new List<int>();

            foreach (var pair in snowballs)
            {
                SnowballState snowball = pair.Value;

                snowball.Position +=
                    snowball.Direction
                    * snowball.Speed
                    * deltaTime;

                float traveledDistance =
                    Vector3.Distance(
                        snowball.SpawnPosition,
                        snowball.Position
                    );

                if (traveledDistance >= snowball.MaxDistance)
                {
                    removeSnowballIds.Add(snowball.Id);
                    continue;
                }

                foreach (var player in clientPositions)
                {
                    int playerId = player.Key;
                    Vector3 playerPosition = player.Value;

                    // 내가 던진 눈덩이에 내가 맞지 않도록
                    if (playerId == snowball.OwnerId)
                        continue;

                    float dx = snowball.Position.X - playerPosition.X;

                    float dz = snowball.Position.Z - playerPosition.Z;

                    float distance = MathF.Sqrt(dx * dx + dz * dz);

                    if (distance <= 0.7f)
                    {
                        HandlePlayerHit(
                            snowball.OwnerId,
                            playerId,
                            snowball.Direction
                        );

                        removeSnowballIds.Add(snowball.Id);
                        break;
                    }
                }
            }

            foreach (int snowballId in removeSnowballIds)
            {
                RemoveSnowball(snowballId);
            }
        }

        void HandlePlayerHit(
            int attackerId,
            int targetId,
            Vector3 direction)
        {
            if (!playerHps.TryGetValue(
                targetId,
                out int currentHp))
            {
                return;
            }

            if (currentHp <= 0)
                return;

            currentHp--;

            playerHps[targetId] = currentHp;

            if (currentHp == 0)
            {
                playerDead[targetId] = true;

                gunLevels[targetId] = 0;
                BroadcastGunRemove(targetId);

                if (snowballCounts.TryGetValue(targetId, out int snowballCount))
                {
                    int dropCount = snowballCount / 2;

                    if (dropCount > 0)
                    {
                        int dropId = ++nextDropSnowballId;

                        DroppedSnowballState drop = new DroppedSnowballState
                        {
                            Id = dropId,
                            Position = clientPositions[targetId],
                            Count = dropCount
                        };

                        droppedSnowballs[dropId] = drop;

                        BroadcastDroppedSnowballs(
                            drop.Id,
                            drop.Position,
                            drop.Count
                        );
                    }

                    snowballCounts[targetId] = 0;
                }

                _ = RespawnPlayer(targetId);
            }
            else
            {
                Vector3 knockback = direction * 5f;

                BroadcastKnockBack(knockback, targetId);
            }

            BroadcastPlayerHit(
                targetId,
                attackerId,
                currentHp
            );
        }

        void BroadcastKnockBack(Vector3 direction, int targetId)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{targetId}:" +
                $"{direction.X}," +
                $"{direction.Y}," +
                $"{direction.Z}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.PlayerKnockback;

            packet[1] = (byte)data.Length;

            Array.Copy(
                data,
                0,
                packet,
                2,
                data.Length
            );

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                if (client.Key != targetId)
                    continue;

                NetworkStream stream =
                    client.Value.GetStream();

                stream.Write(
                    packet,
                    0,
                    packet.Length
                );
            }
        }
        void BroadcastGunRemove(int targetId)
        {
            byte[] data = Encoding.UTF8.GetBytes(targetId.ToString());

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.GunRemoved;

            packet[1] = (byte)data.Length;

            Array.Copy(
                data,
                0,
                packet,
                2,
                data.Length
            );

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream =
                    client.Value.GetStream();

                stream.Write(
                    packet,
                    0,
                    packet.Length
                );
            }
        }
        void BroadcastDroppedSnowballs(int id, Vector3 position, int count)
        {
            string payload =
                $"{id}:" +
                $"{position.X}," +
                $"{position.Z}:" +
                $"{count}";

            byte[] data =
                Encoding.UTF8.GetBytes(payload);

            byte[] packet =
                new byte[data.Length + 2];

            packet[0] =
                (byte)TcpPacketType.DroppedSnowballs;

            packet[1] =
                (byte)data.Length;

            Array.Copy(
                data,
                0,
                packet,
                2,
                data.Length
            );

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream =
                    client.Value.GetStream();

                stream.Write(
                    packet,
                    0,
                    packet.Length
                );
            }
        }
        async Task RespawnPlayer(int clientId)
        {
            await Task.Delay(3000);

            if (!playerSpawnPositions.TryGetValue(
                clientId,
                out Vector3 spawnPosition))
            {
                return;
            }

            playerHps[clientId] = 5;
            playerDead[clientId] = false;
            clientPositions[clientId] = spawnPosition;

            BroadcastPlayerRespawn(
                clientId,
                spawnPosition,
                5
            );
        }

        void BroadcastPlayerRespawn(int targetId, Vector3 spawnPosition, int currentHp)
        {
            string payload =
                $"{targetId}:" +
                $"{spawnPosition.X}," +
                $"{spawnPosition.Y}," +
                $"{spawnPosition.Z}:" +
                $"{currentHp}";

            byte[] data =
                Encoding.UTF8.GetBytes(payload);

            byte[] packet =
                new byte[data.Length + 2];

            packet[0] =
                (byte)TcpPacketType.PlayerRespawn;

            packet[1] =
                (byte)data.Length;

            Array.Copy(
                data,
                0,
                packet,
                2,
                data.Length
            );

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream =
                    client.Value.GetStream();

                stream.Write(
                    packet,
                    0,
                    packet.Length
                );
            }
        }

        void BroadcastPlayerHit(
            int targetId,
            int attackerId,
            int currentHp)
        {
            string payload =
                $"{targetId}:{attackerId}:{currentHp}";

            byte[] data =
                Encoding.UTF8.GetBytes(payload);

            byte[] packet =
                new byte[data.Length + 2];

            packet[0] =
                (byte)TcpPacketType.PlayerHit;

            packet[1] =
                (byte)data.Length;

            Array.Copy(
                data,
                0,
                packet,
                2,
                data.Length
            );

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream =
                    client.Value.GetStream();

                stream.Write(
                    packet,
                    0,
                    packet.Length
                );
            }
        }

        void RemoveSnowball(int snowballId)
        {
            if (!snowballs.TryRemove(
                snowballId,
                out SnowballState snowball))
            {
                return;
            }

            Console.WriteLine(
                $"Snowball {snowballId} 제거"
            );

            BroadcastSnowballDespawn(snowballId);
        }

        void BroadcastSnowballDespawn(int snowballId)
        {
            byte[] data = Encoding.UTF8.GetBytes(snowballId.ToString());

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.SnowballDespawn;

            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                NetworkStream stream =
                    client.Value.GetStream();

                stream.Write(
                    packet,
                    0,
                    packet.Length
                );
            }
        }

        void UpdateCentralSnowball(float deltaTime)
        {
            centralSnowballTimer += deltaTime;

            if (centralSnowballCount >= CentralSnowballMaxCount)
                return;

            if(centralSnowballTimer >= CentralSnowballInterval)
            {
                centralSnowballCount += 1;
                centralSnowballTimer = 0f;

                Console.WriteLine($"Timer: {centralSnowballTimer}" +
                    $"Count: {centralSnowballCount}");
            }
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