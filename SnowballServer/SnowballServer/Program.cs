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

    class CoreState
    {
        public int OwnerClientId;
        public int Hp;
        public Vector3 Position;
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
        GameFinished = 0x1A,

        CentralSnowball = 0x20,
        CentralSnowballResult = 0x21,

        BridgePurchaseRequest = 0x22,
        BridgePurchaseResult = 0x23,
        BridgeOtherPlayerJoin = 0x24,

        CoreHitRequest = 0x25,
        CoreOutPlayer = 0x26,
        CoreHitResult = 0x27,

        WinnerPlayer = 0x28,
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
        private ConcurrentDictionary<int, int> playerBaseKeys = new ConcurrentDictionary<int, int>();

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

        private ConcurrentDictionary<int, Vector3> gunShopPosition = new ConcurrentDictionary<int, Vector3>();

        private ConcurrentDictionary<int, StorageState> storages = new ConcurrentDictionary<int, StorageState>();

        private ConcurrentDictionary<int, Vector3> storagePosition = new ConcurrentDictionary<int, Vector3>();

        private ConcurrentDictionary<int, BridgeState> bridges = new ConcurrentDictionary<int, BridgeState>();

        private ConcurrentDictionary<int, Vector3> bridgePosition = new ConcurrentDictionary<int, Vector3>();

        private ConcurrentDictionary<int, CoreState> cores = new ConcurrentDictionary<int, CoreState>();

        private ConcurrentDictionary<int, bool> playerEliminated = new ConcurrentDictionary<int, bool>();

        private const int BridgePrice = 5;

        private int nextSnowballId = 0;
        private int nextDropSnowballId = 0;
        private int nextClientId = 0;

        private GameState gameState = GameState.Waiting;

        private int centralSnowballCount = 0;
        private float centralSnowballTimer = 0f;
        private const int CentralSnowballMaxCount = 10;

        private const float CentralSnowballInterval = 5f;

        private Vector3 centralSnowballPosition = new Vector3(11, 0, 91.8f);

        private readonly object centralSnowballLock = new object();
        private readonly object playerStateLock = new object();
        private readonly object baseKeyLock = new object();
        private readonly object snowFieldLock = new object();
        private ConcurrentDictionary<TcpClient, object> tcpSendLocks = new();

        public async Task StartGameLoop()
        {
            const float tickRate = 20f;
            const float deltaTime = 1f / tickRate;

            int delayMs = (int)(1000f / tickRate);

            try
            {
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
            catch(Exception ex)
            {
                Console.WriteLine(
                    $"[StartGameLoop Error]\n{ex}");
            }
        }

        public void Connect()
        {
            InitializeCores();
            InitializeSnowField();
            InitializeGunPrice();
            InitializeStorages();
            InitializeBridges();
            InitializeEliminated();
            InitializePlayerSpawnPos();

            gameState = GameState.Waiting;

            // TCP 시작
            StartTcpServer(9050);
            // UDP 시작
            StartUdpServer(9051);
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
        void InitializePlayerSpawnPos()
        {
            playerSpawnPositions[0] = new Vector3(5, 15, 5);
            playerSpawnPositions[1] = new Vector3(93, 15, 85);
            playerSpawnPositions[2] = new Vector3(13, 15, 178);
            playerSpawnPositions[3] = new Vector3(-57, 15, 95);
        }
        void InitializeEliminated()
        {
            for (int i = 0; i < 4; i++)
            {
                playerEliminated[i] = true;
            }
        }
        void InitializeCores()
        {
            for(int i = 0; i < 4; i++)
            {
                cores[i] = new CoreState
                {
                    OwnerClientId = -1,
                    Hp = 5,
                };
            }

            cores[0].Position = new Vector3(5, 0, -16);
            cores[1].Position = new Vector3(114, 0, 85);
            cores[2].Position = new Vector3(13, 0, 200);
            cores[3].Position = new Vector3(-78, 0, 95);
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

            bridgePosition[0] = new Vector3(10, 3, 23);
            bridgePosition[1] = new Vector3(75, 3, 90);
            bridgePosition[2] = new Vector3(8, 3, 160);
            bridgePosition[3] = new Vector3(-39, 3, 90);
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

            storagePosition[0] = new Vector3(4, 0, -3);
            storagePosition[1] = new Vector3(101, 0, 84);
            storagePosition[2] = new Vector3(14, 0, 186);
            storagePosition[3] = new Vector3(-65, 0, 96);
        }

        void InitializeGunPrice()
        {
            gunPrices[0] = 3;
            gunPrices[1] = 10;
            gunPrices[2] = 30;

            gunShopPosition[0] = new Vector3(6, 0, -3);
            gunShopPosition[1] = new Vector3(101, 0, 86);
            gunShopPosition[2] = new Vector3(12, 0, 186);
            gunShopPosition[3] = new Vector3(-65, 0, 94);
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

                MinX = 93,
                MaxX = 98,

                MinZ = 85,
                MaxZ = 90,
            };
            snowFields[2] = new SnowFieldState
            {
                OwnerClientId = -1,

                MinX = 13,
                MaxX = 18,

                MinZ = 178,
                MaxZ = 183,
            };
            snowFields[3] = new SnowFieldState
            {
                OwnerClientId = -1,

                MinX = -57,
                MaxX = -52,

                MinZ = 95,
                MaxZ = 100,
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

                    if (gameState != GameState.Waiting)
                    {
                        SendGameUnavailable(tcpClient);
                        tcpClient.Close();
                        continue;
                    }

                    int clientId = Interlocked.Increment(ref nextClientId);

                    string token = Guid.NewGuid().ToString("N");

                    tokens[token] = clientId;

                    int baseKey = -1;

                    lock (baseKeyLock)
                    {
                        foreach (var bridge in bridges)
                        {
                            if (bridge.Value.OwnerClientId == -1)
                            {
                                baseKey = bridge.Key;
                                bridge.Value.OwnerClientId = clientId;
                                playerBaseKeys[clientId] = baseKey;
                                tcpSendLocks[tcpClient] = new object();
                                break;
                            }
                        }
                    }
                    if (baseKey == -1)
                    {
                        tcpClient.Close();
                        continue;
                    }

                    Console.WriteLine($"클라이언트 {clientId} 연결");

                    SendClientInfo(tcpClient, clientId, token, baseKey);

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
            TcpClient tcpClientToClose = null;

            bool shouldBroadcastLeave = false;
            bool shouldFinishMatch = false;
            bool hasWinner = false;

            int winnerClientId = -1;
            int winnerBaseKey = -1;

            lock (playerStateLock)
            {
                if (tcpClients.TryRemove(clientId, out TcpClient tcpClient))
                {
                    tcpSendLocks.TryRemove(tcpClient, out _);
                    tcpClientToClose = tcpClient;
                    shouldBroadcastLeave = true;
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

                if (playerBaseKeys.TryGetValue(clientId, out int baseKey))
                {
                    shouldBroadcastLeave = true;

                    if (snowFields.TryGetValue(
                        baseKey,
                        out SnowFieldState snowField))
                    {
                        snowField.OwnerClientId = -1;
                    }

                    if (bridges.TryGetValue(
                        baseKey,
                        out BridgeState bridge))
                    {
                        bridge.OwnerClientId = -1;
                        bridge.IsPurchased = false;
                    }

                    if (storages.TryGetValue(
                        baseKey,
                        out StorageState storage))
                    {
                        storage.OwnerClientId = -1;
                        storage.SnowballCount = 0;
                    }

                    playerEliminated[baseKey] = true;
                }

                if (gameState == GameState.Playing)
                {
                    int aliveCount = 0;

                    foreach (var player in playerBaseKeys)
                    {
                        int currentClientId = player.Key;
                        int currentBaseKey = player.Value;

                        if (playerEliminated.TryGetValue(
                            currentBaseKey,
                            out bool eliminated) &&
                            !eliminated)
                        {
                            aliveCount++;

                            winnerClientId = currentClientId;
                            winnerBaseKey = currentBaseKey;
                        }
                    }

                    if (aliveCount == 1)
                    {
                        gameState = GameState.Finished;

                        hasWinner = true;
                        shouldFinishMatch = true;
                    }
                    else if (aliveCount == 0)
                    {
                        gameState = GameState.Finished;

                        shouldFinishMatch = true;
                    }
                }
            }

            tcpClientToClose?.Close();

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
                tokens.TryRemove(tokenToRemove, out _);

            if (shouldBroadcastLeave)
                BroadcastPlayerLeave(clientId);

            Console.WriteLine($"클라이언트 {clientId} 정리 완료");

            if (hasWinner)
            {
                BroadcastWinnerPlayer(
                    winnerClientId,
                    winnerBaseKey);
            }

            if (shouldFinishMatch)
            {
                _ = FinishMatch();
            }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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

                case (byte)TcpPacketType.CoreHitRequest:
                    HandleCoreHitRequest(packet, clientId);
                    break;

                default:
                    Console.WriteLine("알 수 없는 TCP 패킷 타입: " + packetType);
                    break;
            }
        }

        void SendGameUnavailable(TcpClient client)
        {
            byte[] packet = new byte[2];

            packet[0] = (byte)TcpPacketType.GameFinished;
            packet[1] = 0;

            if (!client.Connected) return;


            try
            {
                NetworkStream stream = client.GetStream();
                stream.Write(packet, 0, packet.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[TCP Send Error]\n{ex}");
            }
        }
        void SendClientInfo(TcpClient client, int clientId, string token, int baseKey)
        {
            string payload = $"{clientId}:{token}:{baseKey}";
            byte[] data = Encoding.UTF8.GetBytes(payload);

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.AssignClientId;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            if (!client.Connected)
                return;

            if (!tcpSendLocks.TryGetValue(
                client,
                out object sendLock))
                return;

            lock (sendLock)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(packet, 0, packet.Length);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(
                        $"[TCP Send Error] Client:{clientId}\n{ex}");
                }
            }
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

            if (playerBaseKeys.TryGetValue(clientId, out int baseKey))
            {
                snowFields[baseKey].OwnerClientId = clientId;

                int posX = Random.Shared.Next(snowFields[baseKey].MinX, snowFields[baseKey].MaxX);
                int posZ = Random.Shared.Next(snowFields[baseKey].MinZ, snowFields[baseKey].MaxZ);

                snowFields[baseKey].CurrentSnowPosition = new Vector3(posX, 3, posZ);

                BroadcastSnowFieldInfo(clientId, baseKey, snowFields[baseKey].CurrentSnowPosition);

                storages[baseKey].OwnerClientId = clientId;

                storages[baseKey].SnowballCount = 0;

                bridges[baseKey].OwnerClientId = clientId;
                bridges[baseKey].IsPurchased = false;

                cores[baseKey].OwnerClientId = clientId;
                cores[baseKey].Hp = 5;

                BroadcastStorageInfo(clientId, baseKey);
            }

            foreach (var field in snowFields)
            {
                if (field.Value.OwnerClientId == -1 || field.Value.OwnerClientId == clientId)
                    continue;

                SendSnowFieldToClient(
                    tcpClient,
                    field.Value.OwnerClientId,
                    field.Key,
                    field.Value.CurrentSnowPosition
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
                    gunLevel.Key,
                    gunLevel.Value,
                    snowballCounts[gunLevel.Key]
                );
            }
        }
        void SendGunInfoToClient(TcpClient tcpClient, int onwerId, int gunLevel, int snowballCount)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{onwerId}" +
                $"{gunLevel}" +
                $"{snowballCount}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.GunPurchaseResult;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            if (!tcpSendLocks.TryGetValue(
                tcpClient,
                out object sendLock))
                return;

            lock (sendLock)
            {
                try
                {
                    if (tcpClient.Connected)
                    {
                        NetworkStream stream = tcpClient.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[TCP Send Error]\n{ex}");
                }
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

            if (!tcpSendLocks.TryGetValue(
                tcpClient,
                out object sendLock))
                return;

            lock (sendLock)
            {
                try
                {
                    if (tcpClient.Connected)
                    {
                        NetworkStream stream = tcpClient.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[TCP Send Error]\n{ex}");
                }
            }
        }
        void SendSnowFieldToClient(TcpClient tcpClient, int onwerId, int fieldKey, Vector3 pos)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{onwerId}:" +
                $"{fieldKey}:" +
                $"{pos.X}," +
                $"{pos.Y}," +
                $"{pos.Z}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.SnowFieldJoin;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);

            if (!tcpSendLocks.TryGetValue(
                tcpClient,
                out object sendLock))
                return;

            lock (sendLock)
            {
                try
                {
                    if (tcpClient.Connected)
                    {
                        NetworkStream stream = tcpClient.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[TCP Send Error]\n{ex}");
                }
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
                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        if (client.Value.Connected)
                        {
                            NetworkStream stream = client.Value.GetStream();
                            stream.Write(packet, 0, packet.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }
        void BroadcastSnowFieldInfo(int senderID, int fieldKey, Vector3 pos)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{senderID}:" +
                $"{fieldKey}:" +
                $"{pos.X}," +
                $"{pos.Y}," +
                $"{pos.Z}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.SnowFieldJoin;
            packet[1] = (byte)data.Length;
            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)//새로운 클라이언트를 이미 접속한 모든 클라이언트에 전송
            {
                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        if (client.Value.Connected)
                        {
                            NetworkStream stream = client.Value.GetStream();
                            stream.Write(packet, 0, packet.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        if (client.Value.Connected)
                        {
                            NetworkStream stream = client.Value.GetStream();
                            stream.Write(packet, 0, packet.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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
                if (client.Key == senderID)
                    continue;

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        if (client.Value.Connected)
                        {
                            NetworkStream stream = client.Value.GetStream();
                            stream.Write(packet, 0, packet.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
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

                        if (!tcpSendLocks.TryGetValue(
                            client.Value,
                            out object sendLock))
                            continue;

                        lock (sendLock)
                        {
                            try
                            {
                                NetworkStream stream = tcpClients[senderID].GetStream();
                                stream.Write(packet, 0, packet.Length);
                                Console.WriteLine("서버 : 색, 이름 보냄");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    $"[TCP Send Error]\n{ex}");
                            }
                        }
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

            if(y < -5f)
            {
                SendCoreDeadResult(clientId);
                return;
            }

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
                if (client.Key == senderID)
                    continue;


                if (!tcpClients.TryGetValue(client.Key, out TcpClient tcpClient))
                    continue;

                if (!tcpClient.Connected)
                    continue;

                try
                {
                    udpServer.Send(
                        packet,
                        packet.Length,
                        client.Value
                    );
                }
                catch (Exception e)
                {
                    Console.WriteLine($"UDP 전송 실패: {e}");
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
            Console.WriteLine("Game Start");

            centralSnowballCount = 0;
            centralSnowballTimer = 0f;

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                {
                    continue;
                }


                Console.WriteLine($"hp: {playerHps[client.Key]}");
                Console.WriteLine($"snowballCount: {snowballCounts[client.Key]}");
                Console.WriteLine($"gunLevel: {gunLevels[client.Key]}");
                Console.WriteLine($"storage: {storages[playerBaseKeys[client.Key]].SnowballCount}");
                Console.WriteLine($"playerDead: {playerDead[client.Key]}");

                storages[playerBaseKeys[client.Key]].SnowballCount = 0;
                bridges[playerBaseKeys[client.Key]].IsPurchased = false;

                playerEliminated[playerBaseKeys[client.Key]] = false;

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
                    $"storage: {storages[playerBaseKeys[client.Key]].SnowballCount}" +
                    $"playerDead: {playerDead[client.Key]}");

            }

            gameState = GameState.Playing;

            BroadcastGameStart();
        }

        void HandleCoreHitRequest(byte[] buffer, int clientId)
        {
            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
            {
                return;
            }

            int messageLength = buffer[1];

            string data = Encoding.UTF8.GetString(buffer, 2, messageLength);

            string[] parts = data.Split(':', 2);

            if (parts.Length != 2)
                return;

            if (!int.TryParse(parts[0], out int ownerId) ||
                !int.TryParse(parts[1], out int targetBaseKey))
            {
                return;
            }

            if (ownerId == clientId) return;


            int coreHp;
            int aliveCount = 0;
            int winnerClientId = -1;
            int winnerBaseKey = -1;
            bool coreDestroyed = false;
            bool matchFinished = false;

            lock (playerStateLock)
            {
                if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
                {
                    return;
                }

                if (gameState != GameState.Playing)
                    return;

                if (!cores.TryGetValue(
                targetBaseKey,
                out CoreState targetCore))
                {
                    return;
                }

                if (targetCore.OwnerClientId != ownerId)
                {
                    return;
                }

                float dx = cores[targetBaseKey].Position.X - playerPosition.X;
                float dz = cores[targetBaseKey].Position.Z - playerPosition.Z;

                float distance = MathF.Sqrt(dx * dx + dz * dz);

                if (distance > 4f)
                {
                    return;
                }

                if (!playerBaseKeys.TryGetValue(clientId, out int baseKey))
                {
                    return;
                }

                if (baseKey == targetBaseKey) return;

                if (!playerEliminated.TryGetValue(targetBaseKey, out bool isEliminated))
                {
                    return;
                }
                if (isEliminated)
                    return;

                coreHp = targetCore.Hp - 1;
                targetCore.Hp = coreHp;

                if (coreHp <= 0)
                {
                    coreDestroyed = true;

                    //탈락
                    playerEliminated[targetBaseKey] = true;

                    foreach (var player in playerEliminated)
                    {
                        if (player.Value)
                            continue;

                        aliveCount++;
                        winnerBaseKey = player.Key;
                    }

                    if (aliveCount == 1)
                    {
                        foreach (var player in playerBaseKeys)
                        {
                            if (player.Value == winnerBaseKey)
                            {
                                winnerClientId = player.Key;
                                break;
                            }
                        }

                        gameState = GameState.Finished;
                        matchFinished = true;
                    }
                }
            }

            if (coreDestroyed)
            {
                BroadcastCoreOutPlayer(ownerId, targetBaseKey);//플레이어 탈락

                if (matchFinished)
                {
                    BroadcastWinnerPlayer(winnerClientId, winnerBaseKey);

                    _ = FinishMatch();
                }
                else
                {
                    BroadcastCoreHitResult(ownerId, targetBaseKey, coreHp, clientId);//core 줄이기
                    SendCoreDeadResult(clientId);
                }
                
            }
            else
            {
                BroadcastCoreHitResult(ownerId, targetBaseKey, coreHp, clientId);//core 줄이기
                SendCoreDeadResult(clientId);
            }
        }

        async Task FinishMatch()
        {
            try
            {
                gameState = GameState.Finished;

                await Task.Delay(5000);

                ClearPlayers();
                ResetMatch();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FinishMatch Error]\n{ex}");
            }
        }
        void ResetMatch()
        {
            gameState = GameState.Waiting;

            centralSnowballCount = 0;
            centralSnowballTimer = 0f;

            droppedSnowballs.Clear();

            foreach (var core in cores)
            {
                core.Value.OwnerClientId = -1;
                core.Value.Hp = 5;
            }

            foreach (var field in snowFields)
            {
                field.Value.OwnerClientId = -1;

                // CurrentSnowPosition도
                // 필요하면 최초 위치/랜덤 위치로 초기화
            }

            foreach (var bridge in bridges)
            {
                bridge.Value.OwnerClientId = -1;
                bridge.Value.IsPurchased = false;
            }

            foreach (var storage in storages)
            {
                storage.Value.OwnerClientId = -1;
                storage.Value.SnowballCount = 0;
            }

            for (int i = 0; i < 4; i++)
            {
                playerEliminated[i] = true;
            }

            Console.WriteLine("Match Reset");
        }
        void ClearPlayers()
        {
            foreach (var client in tcpClients)
            {
                try
                {
                    client.Value.Close();
                }
                catch { }
            }

            tcpClients.Clear();
            udpClients.Clear();

            tokens.Clear();

            clientPositions.Clear();
            playerBaseKeys.Clear();

            playerHps.Clear();
            snowballCounts.Clear();
            gunLevels.Clear();
            playerDead.Clear();
        }
        void SendCoreDeadResult(int targetId)
        {
            int dropCount = 0;
            int dropId = -1;
            Vector3 dropPosition = Vector3.Zero;

            lock (playerStateLock)
            {
                if (playerDead.TryGetValue(targetId, out bool isDead) &&
                    isDead)
                {
                    return;
                }

                playerDead[targetId] = true;

                gunLevels[targetId] = 0;

                if (snowballCounts.TryGetValue(targetId, out int snowballCount))
                {
                    dropCount = snowballCount / 2;
                    snowballCounts[targetId] = 0;

                    if (dropCount > 0 && 
                        clientPositions.TryGetValue(
                        targetId,
                        out dropPosition))
                    {
                        dropId = ++nextDropSnowballId;

                        droppedSnowballs[dropId] = new DroppedSnowballState
                        {
                            Id = dropId,
                            Position = clientPositions[targetId],
                            Count = dropCount
                        };
                    }
                }
            }

            BroadcastGunRemove(targetId);

            if(dropId != -1)
            {
                BroadcastDroppedSnowballs(
                    dropId,
                    dropPosition,
                    dropCount
                );
            }

            _ = RespawnPlayer(targetId);
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

            int messageLength = buffer[1];

            string data = Encoding.UTF8.GetString(buffer, 2, messageLength);

            if (!int.TryParse(data, out int targetKey))
            {
                return;
            }

            if (!bridgePosition.TryGetValue(targetKey, out Vector3 targetBridgePosition))
                return;

            float dx = targetBridgePosition.X - playerPosition.X;

            float dz = targetBridgePosition.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 15f)
            {
                return;
            }

            int snowballCount = 0;
            lock (playerStateLock)
            {
                if (!snowballCounts.TryGetValue(
                    clientId,
                    out snowballCount))
                    return;

                if (snowballCount < BridgePrice)
                    return;

                if (!bridges.TryGetValue(targetKey, out BridgeState bridge))
                    return;

                if (bridge.IsPurchased) return;

                bridge.IsPurchased = true;

                snowballCount -= BridgePrice;
                snowballCounts[clientId] = snowballCount;
            }

            BroadcastBridgePurchaseResult(targetKey, clientId, snowballCount);
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

            if (gameState != GameState.Playing)
                return;

            int acquiredCount;

            lock (centralSnowballLock)
            {
                if (centralSnowballCount <= 0)
                    return;

                acquiredCount = centralSnowballCount;

                centralSnowballCount = 0;
                centralSnowballTimer = 0f;
            }

            int snowballCount;

            lock (playerStateLock)
            {
                if (!snowballCounts.TryGetValue(
                    clientId,
                    out snowballCount))
                    return;

                snowballCount += acquiredCount;
                snowballCounts[clientId] = snowballCount;
            }

            BroadcastCentralSnowballResult(clientId, snowballCount);
        }

        void HandleStorageRequest(byte[] buffer, int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

            if (!playerBaseKeys.TryGetValue(
                clientId,
                out int playerBaseKey))
            {
                return;
            }

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
                return;

            if (!storagePosition.TryGetValue(
                playerBaseKey,
                out Vector3 targetStoragePosition))
            {
                return;
            }

            float dx = targetStoragePosition.X - playerPosition.X;

            float dz = targetStoragePosition.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 2f)
                return;

            StorageAction action = (StorageAction)buffer[2];

            int storageCount;
            int snowballCount;

            lock (playerStateLock)
            {
                if (!snowballCounts.TryGetValue(
                    clientId,
                    out snowballCount))
                    return;

                if (!storages.TryGetValue(
                    playerBaseKey,
                    out StorageState storage))
                {
                    return;
                }

                storageCount = storage.SnowballCount;

                switch (action)
                {
                    case StorageAction.DepositAll:
                        if (snowballCount <= 0) return;

                        storageCount += snowballCounts[clientId];
                        snowballCount = 0;
                        break;

                    case StorageAction.DepositHalf:
                        if (snowballCount <= 0) return;

                        int amountDeposit = snowballCount / 2;

                        storageCount += amountDeposit;
                        snowballCount -= amountDeposit;
                        break;

                    case StorageAction.WithdrawAll:
                        if (storageCount <= 0) return;

                        snowballCount += storageCount;
                        storageCount = 0;
                        break;

                    case StorageAction.WithdrawHalf:
                        if (storageCount <= 0) return;

                        int amountWithdraw = storageCount / 2;

                        snowballCount += amountWithdraw;
                        storageCount -= amountWithdraw;
                        break;

                    default:
                        break;
                }

                storage.SnowballCount = storageCount;

                snowballCounts[clientId] = snowballCount;
            }
            Console.WriteLine("보관: " + storageCount);
            Console.WriteLine("소유: " + snowballCount);

            SendStorageActionResult(clientId, playerBaseKey, storageCount, snowballCount);

            return;
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

            if (!playerBaseKeys.TryGetValue(clientId, out int baseKey))
                return;

            if (!gunShopPosition.TryGetValue(baseKey, out Vector3 shopPosition))
                return;

            float dx = shopPosition.X - playerPosition.X;

            float dz = shopPosition.Z - playerPosition.Z;

            float distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance > 2f)
                return;

            int snowballCount;
            int gunLevel;
            lock (playerStateLock)
            {
                if (!snowballCounts.TryGetValue(
                    clientId,
                    out snowballCount))
                    return;

                if (!gunLevels.TryGetValue(clientId, out gunLevel))
                    return;

                if (gunLevel > 2)
                    return;

                if (!gunPrices.TryGetValue(gunLevel, out int gunPrice))
                    return;

                if (snowballCount < gunPrice)
                    return;

                snowballCount -= gunPrice;
                gunLevel++;
                snowballCounts[clientId] = snowballCount;
                gunLevels[clientId] = gunLevel;
            }

            BroadcastGunPurchaseResult(clientId, snowballCount, gunLevel);
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }

        void BroadcastCoreHitResult(int ownerId, int targetBaseKey, int hp, int deadClientId)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{ownerId}:" +
                $"{targetBaseKey}:" +
                $"{hp}:" +
                $"{deadClientId}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.CoreHitResult;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }
        void BroadcastCoreOutPlayer(int ownerId, int targetBaseKey)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{ownerId}:" +
                $"{targetBaseKey}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.CoreOutPlayer;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }
        void BroadcastWinnerPlayer(int ownerId, int targetBaseKey)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{ownerId}:" +
                $"{targetBaseKey}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.WinnerPlayer;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }
        void BroadcastBridgePurchaseResult(int targetKey, int clientId, int snowCount)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{targetKey}:" +
                $"{clientId}:" +
                $"{snowCount}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.BridgePurchaseResult;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }

        void BroadcastCentralSnowballResult(int clientId, int snowballCount)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{clientId}:" +
                $"{snowballCount}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.CentralSnowballResult;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }

        void BroadcastGunPurchaseResult(int clientId, int snowballCount, int gunLevel)
        {
            byte[] data = Encoding.UTF8.GetBytes(
                $"{clientId}:" +
                $"{gunLevel}:" +
                $"{snowballCount}");

            byte[] packet = new byte[data.Length + 2];

            packet[0] = (byte)TcpPacketType.GunPurchaseResult;
            packet[1] = (byte)data.Length;

            Array.Copy(data, 0, packet, 2, data.Length);

            foreach (var client in tcpClients)
            {
                if (!client.Value.Connected)
                    continue;

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }

        void HandleSnowItemRequest(byte[] buffer, int clientId)
        {
            if (playerDead.TryGetValue(clientId, out bool isDead) && isDead)
            {
                return;
            }

            if (!clientPositions.TryGetValue(
                clientId,
                out Vector3 playerPosition))
                return;


            foreach (var field in snowFields)
            {
                if (field.Value.OwnerClientId == clientId)
                {
                    int newCount;
                    Vector3 newPosition;

                    lock (snowFieldLock)
                    {
                        float dx = field.Value.CurrentSnowPosition.X - playerPosition.X;

                        float dz = field.Value.CurrentSnowPosition.Z - playerPosition.Z;

                        float distance = MathF.Sqrt(dx * dx + dz * dz);

                        if (distance > 3.5f)
                            return;

                        newCount = snowballCounts.AddOrUpdate(
                            clientId,
                            1,
                            (_, current) => current + 1);

                        int posX = Random.Shared.Next(field.Value.MinX, field.Value.MaxX);

                        int posZ = Random.Shared.Next(field.Value.MinZ, field.Value.MaxZ);

                        newPosition = new Vector3(posX, 3f, posZ);

                        field.Value.CurrentSnowPosition = newPosition;

                        Console.WriteLine(
                            $"Client {clientId} SnowFieldItem 획득 / Snowball {newCount}"
                        );
                    }
                    BroadcastSnowItemResult(
                        clientId,
                        field.Key,
                        newCount,
                        newPosition);

                    break;
                }
            }
        }

        void BroadcastSnowItemResult(
            int winnerClientId,
            int fieldKey,
            int snowballCount,
            Vector3 pos)
        {
            string payload =
                $"{winnerClientId}:" +
                $"{fieldKey}:" +
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }

        void HandleSnowballThrowRequest(int clientId)
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

            if (!clientYaws.TryGetValue(
                clientId,
                out float yaw))
            {
                return;
            }


            int gunLevel;
            lock (playerStateLock)
            {
                if (!gunLevels.TryGetValue(clientId, out gunLevel))
                    return;

                if (gunLevel == 0)
                    return;

                if (!snowballCounts.TryGetValue(
                    clientId,
                    out int snowballCount))
                {
                    return;
                }

                if (snowballCount <= 0)
                    return;

                snowballCounts[clientId] = snowballCount - 1;
            }

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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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
            int newHp;
            bool died = false;
            int dropCount = 0;
            Vector3 dropPosition = Vector3.Zero;
            int dropId = -1;

            lock (playerStateLock)
            {
                if (!playerHps.TryGetValue(
                    targetId,
                    out int currentHp))
                {
                    return;
                }
                if (currentHp <= 0)
                    return;


                if (playerDead.TryGetValue(
                    targetId,
                    out bool isDead) &&
                    isDead)
                    return;

                newHp = currentHp - 1;
                playerHps[targetId] = newHp;


                if (newHp <= 0)
                {
                    died = true;

                    playerDead[targetId] = true;
                    gunLevels[targetId] = 0;

                    if (snowballCounts.TryGetValue(targetId, out int snowballCount))
                    {
                        dropCount = snowballCount / 2;

                        snowballCounts[targetId] = 0;
                    }


                    if (dropCount > 0 && clientPositions.TryGetValue(targetId, out dropPosition))
                    {
                        dropId = Interlocked.Increment(ref nextDropSnowballId);

                        droppedSnowballs[dropId] = new DroppedSnowballState
                        {
                            Id = dropId,
                            Position = clientPositions[targetId],
                            Count = dropCount
                        };
                    }
                }
            }


            if (died)
            {
                BroadcastGunRemove(targetId);


                if (dropId != -1)
                {
                    BroadcastDroppedSnowballs(
                        dropId,
                        dropPosition,
                        dropCount
                    );
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
                newHp
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }
        async Task RespawnPlayer(int clientId)
        {
            try
            {
                await Task.Delay(3000);

                if (!playerSpawnPositions.TryGetValue(
                    playerBaseKeys[clientId],
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
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[RespawnPlayer Error]  clientId:{clientId}\n{ex}");
            }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
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

                if (!tcpSendLocks.TryGetValue(
                    client.Value,
                    out object sendLock))
                    continue;

                lock (sendLock)
                {
                    try
                    {
                        NetworkStream stream = client.Value.GetStream();
                        stream.Write(packet, 0, packet.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[TCP Send Error]\n{ex}");
                    }
                }
            }
        }

        void UpdateCentralSnowball(float deltaTime)
        {
            lock (centralSnowballLock)
            {
                centralSnowballTimer += deltaTime;

                if (centralSnowballCount >= CentralSnowballMaxCount)
                    return;

                if (centralSnowballTimer >= CentralSnowballInterval)
                {
                    centralSnowballCount += 1;
                    centralSnowballTimer = 0f;

                    Console.WriteLine($"Timer: {centralSnowballTimer}" +
                        $"Count: {centralSnowballCount}");
                }
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