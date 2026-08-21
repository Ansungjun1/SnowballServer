using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.Text;
using TMPro;
using System.Threading;
using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine.TextCore.Text;

enum TcpPacketType : byte
{
    Chat = 0x01,
    PlayerJoin = 0x02,
    AssignClientId = 0x03,
    PlayerLeave = 0x04,

    SnowItemRequest = 0x05,
    SnowItemResult = 0x06,
    SnowItemRespawn = 0x07,
    SnowballThrowRequest = 0x08,
    SnowballSpawn = 0x09,
    SnowballDespawn = 0x0A,
    PlayerHit = 0x0B,
}

enum UdpPacketType : byte
{
    Position = 0x02
}

public class NetworkClient : MonoBehaviour
{
    private TcpClient tcpServer;
    private UdpClient udpClient;
    private NetworkStream stream;
    public TextMeshProUGUI text;
    public GameObject character;
    private Dictionary<int, GameObject> otherPlayers = new Dictionary<int, GameObject>();
    private Dictionary<int, int> lastReceivedPositionSequence = new Dictionary<int, int>();
    public GameObject[] Prefab;
    private int ChoosePrefab;
    public TextMeshProUGUI chat_text;
    public TextMeshProUGUI my_Chat_text;
    public TMP_InputField input_Chat_text;
    public Transform[] SpawnPos;

    private Dictionary<int, SnowItem> snowItems = new Dictionary<int, SnowItem>();

    private bool isRunning;

    private readonly object lockObject = new object();  // lock을 위한 객체

    private int clientId = -1;
    private string sessionToken;
    private bool sessionReady = false;

    //private string ServerIP = "58.231.147.182";
    private string ServerIP = "192.168.0.5";
    private int udpPort = 9051;
    private int tcpPort = 9050;

    bool isChatting;

    private int positionSequence = 0;

    private int mySnowballCount = 0;

    private Dictionary<int, Vector3> targetPositions = new Dictionary<int, Vector3>();
    private Dictionary<int, float> targetYaws = new Dictionary<int, float>();
    private float positionSendInterval = 0.05f; // 20Hz
    private float positionSendTimer = 0f;

    public GameObject snowballPrefab;

    private Dictionary<int, GameObject> snowballObjects = new Dictionary<int, GameObject>();

    private void Start()
    {
        System.Random rand = new System.Random();
        ChoosePrefab = rand.Next(0, Prefab.Length);
    }
    public void Connect()
    {
        isRunning = true;

        ConnectToServer(ServerIP, tcpPort);
        ConnectToUdpServer(ServerIP, udpPort);

        RegisterSnowItems();
    }

    void ConnectToServer(string serverIP, int port)
    {
        //서버 연결
        tcpServer = new TcpClient();
        tcpServer.Connect(serverIP, port);
        stream = tcpServer.GetStream();

        _ = ListenForServerMessages();
    }

    void ConnectToUdpServer(string serverIP, int port)
    {
        udpClient = new UdpClient();
        udpClient.Connect(serverIP, port);
        Debug.Log("UDP서버 연결");

        _ = ListenForUdpMessage();
    }
    void RegisterSnowItems()
    {
        SnowItem[] items = FindObjectsOfType<SnowItem>();

        foreach (SnowItem item in items)
        {
            snowItems[item.itemId] = item;
        }
    }
    private void Update()
    {
        if (!isRunning || character == null)
            return;


        if (!isChatting)
        {
            if (Input.GetKey(KeyCode.W)) character.GetComponent<PlayerMovement>().MovePlayer(KeyCode.W);
            else if (Input.GetKeyUp(KeyCode.W)) character.GetComponent<PlayerMovement>().MovePlayer("W");
            if (Input.GetKey(KeyCode.D)) character.GetComponent<PlayerMovement>().MovePlayer(KeyCode.D);
            else if (Input.GetKeyUp(KeyCode.D)) character.GetComponent<PlayerMovement>().MovePlayer("A");
            if (Input.GetKey(KeyCode.S)) character.GetComponent<PlayerMovement>().MovePlayer(KeyCode.S);
            else if (Input.GetKeyUp(KeyCode.S)) character.GetComponent<PlayerMovement>().MovePlayer("W");
            if (Input.GetKey(KeyCode.A)) character.GetComponent<PlayerMovement>().MovePlayer(KeyCode.A);
            else if (Input.GetKeyUp(KeyCode.A)) character.GetComponent<PlayerMovement>().MovePlayer("A");

            if (Input.GetKeyDown(KeyCode.Return))
            {
                isChatting = true;

                input_Chat_text.gameObject.SetActive(true);

                input_Chat_text.Select();
                input_Chat_text.ActivateInputField();
            }
            else if(Input.GetMouseButtonDown(0))
            {
                RequestThrowSnowball();
            }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Return))
                HandlePlayerChat();
        }


        positionSendTimer += Time.deltaTime;

        if (positionSendTimer >= positionSendInterval)
        {
            positionSendTimer -= positionSendInterval;

            SendPositionToServer(character.transform.position);
        }

        UpdateRemotePlayers();
    }

    private void FixedUpdate()
    {
        if (isRunning)
        {
            if (character != null)
            {
                PlayerState state = character.GetComponent<PlayerState>();

                if(state != null && !state.IsDead)
                    character.GetComponent<PlayerMovement>().SetMovePlayer();
            }
        }
    }

    void SendPositionToServer(Vector3 position)
    {
        if (!sessionReady) return;

        positionSequence++;

        float yaw = character.transform.eulerAngles.y;

        PlayerMovement movement = character.GetComponent<PlayerMovement>();

        string positionData =
            sessionToken + ":"
            + positionSequence + ":"
            + position.x + ","
            + position.y + ","
            + position.z + ":"
            + yaw + ":"
            + (movement.IsMoving ? "1" : "0");

        byte[] data = Encoding.UTF8.GetBytes(positionData);

        byte[] packet = new byte[data.Length + 2];
        packet[0] = (byte)UdpPacketType.Position;
        packet[1] = (byte)data.Length;

        Array.Copy(data, 0, packet, 2, data.Length);

        udpClient.Send(packet, packet.Length);

        text.text += "서버로 내 위치 전송" + "\n";
    }

    public void HandlePlayerChat()
    {
        string message = input_Chat_text.text;

        input_Chat_text.text = "";
        input_Chat_text.DeactivateInputField();

        isChatting = false;

        if (string.IsNullOrEmpty(message))
            return;

        byte[] data = Encoding.UTF8.GetBytes(message);

        if (data.Length > byte.MaxValue)
        {
            Debug.Log("채팅 데이터가 너무 깁니다.");
            return;
        }

        my_Chat_text.text = message;

        SendChatMessageToServer(data);
    }

    void SendChatMessageToServer(byte[] data)
    {
        byte[] packet = new byte[data.Length + 2];

        packet[0] = 0x01;
        packet[1] = (byte)data.Length;
        Array.Copy(data, 0, packet, 2, data.Length);
        stream.Write(packet, 0, packet.Length);
        text.text += "서버로 내 채팅 전송" + "\n";
    }

    async Task ListenForServerMessages()
    {
        byte[] readBuffer = new byte[1024];
        List<byte> receiveBuffer = new List<byte>();

        Debug.Log("클라이언트 : tcp연결");

        while (isRunning)
        {
            int bytesRead = 0;

            try
            {
                bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
            }
            catch (Exception e)
            {
                Debug.Log("연결 끊김: " + e.Message);
                break;
            }

            if (bytesRead == 0)
                break;

            for (int i = 0; i < bytesRead; i++)
            {
                receiveBuffer.Add(readBuffer[i]);
            }

            ProcessTcpPackets(
                receiveBuffer,
                clientId
            );
        }
    }

    async Task ListenForUdpMessage()
    {
        while (isRunning)
        {
            try
            {
                UdpReceiveResult result = await udpClient.ReceiveAsync();
                byte[] buffer = result.Buffer;


                if (buffer == null || buffer.Length < 2)
                {
                    Debug.Log("잘못된 UDP 패킷");
                    continue;
                }

                byte packetType = buffer[0];
                int messageLength = buffer[1];

                if (buffer.Length != 2 + messageLength)
                {
                    Debug.Log("UDP 패킷 길이 오류");
                    continue;
                }

                switch (packetType)
                {
                    //위치
                    case (byte)UdpPacketType.Position:
                        UpdatePositionFromServer(buffer);
                        break;

                    default:
                        Debug.Log("알 수 없는 패킷 타입:" + packetType);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.Log("UDP 요청 수신 실패: " + e);
            }
        }
    }
    void ProcessTcpPackets(List<byte> receiveBuffer, int clientId)
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
                clientId
            );

            // while 반복
            // Buffer 안에 Packet이 더 있으면 계속 처리
        }
    }

    void HandleTcpPacket(byte packetType, byte[] packet, int clientId)
    {
        switch (packetType)
        {
            //채팅
            case (byte)TcpPacketType.Chat:
                UpdateChatFromServer(packet);
                break;

            case (byte)TcpPacketType.PlayerJoin:
                ClientColorFromServer(packet);
                break;

            case (byte)TcpPacketType.AssignClientId:
                SetClientIdFromServer(packet);
                break;

            case (byte)TcpPacketType.PlayerLeave:
                RemoveOtherPlayer(packet);
                break;

            case (byte)TcpPacketType.SnowItemResult:
                HandleSnowItemResult(packet);
                break;

            case (byte)TcpPacketType.SnowItemRespawn:
                HandleSnowItemRespawn(packet);
                break;

            case (byte)TcpPacketType.SnowballSpawn:
                HandleSnowballSpawn(packet);
                break;

            case (byte)TcpPacketType.SnowballDespawn:
                HandleSnowballDespawn(packet);
                break;

            case (byte)TcpPacketType.PlayerHit:
                HandlePlayerHit(packet);
                break;

            default:
                Debug.Log("알 수 없는 패킷 타입:" + packetType);
                break;
        }
    }

    void HandlePlayerHit(byte[] buffer)
    {
        int messageLength = buffer[1];

        string data =
            Encoding.UTF8.GetString(
                buffer,
                2,
                messageLength
            );

        string[] parts = data.Split(':');

        if (parts.Length != 3)
            return;

        if (!int.TryParse(parts[0], out int targetId) ||
            !int.TryParse(parts[1], out int attackerId) ||
            !int.TryParse(parts[2], out int currentHp))
        {
            return;
        }

        UnityMainThreadDispatcher.Enqueue(() =>
        {
            ApplyPlayerHit(
                targetId,
                currentHp
            );
        });
    }

    void ApplyPlayerHit(
        int targetId,
        int currentHp)
    {
        GameObject targetPlayer;

        if (targetId == clientId)
        {
            targetPlayer = character;
        }
        else if (!otherPlayers.TryGetValue(
            targetId,
            out targetPlayer))
        {
            return;
        }

        PlayerState state = targetPlayer.GetComponent<PlayerState>();

        if (state != null)
        {
            state.SetHp(currentHp);
            state.PlayHit();
        }
    }

    void HandleSnowballDespawn(byte[] buffer)
    {
        int messageLength = buffer[1];

        string data =
            Encoding.UTF8.GetString(
                buffer,
                2,
                messageLength
            );

        if (!int.TryParse(
            data,
            out int snowballId))
        {
            return;
        }

        UnityMainThreadDispatcher.Enqueue(() =>
        {
            RemoveSnowballObject(snowballId);
        });
    }

    void RemoveSnowballObject(int snowballId)
    {
        if (!snowballObjects.TryGetValue(
            snowballId,
            out GameObject snowball))
        {
            return;
        }

        Destroy(snowball);

        snowballObjects.Remove(snowballId);
    }

    void HandleSnowItemResult(byte[] buffer)
    {
        int messageLength = buffer[1];

        string data =
            Encoding.UTF8.GetString(buffer, 2, messageLength);

        string[] parts = data.Split(':');

        if (parts.Length != 3)
            return;

        if (!int.TryParse(parts[0], out int itemId) ||
            !int.TryParse(parts[1], out int winnerClientId) ||
            !int.TryParse(parts[2], out int snowballCount))
        {
            return;
        }

        UnityMainThreadDispatcher.Enqueue(() =>
        {
            if (snowItems.TryGetValue(itemId, out SnowItem item))
            {
                item.gameObject.SetActive(false);
            }

            if (winnerClientId == clientId)
            {
                mySnowballCount = snowballCount;

                Debug.Log(
                    $"눈덩이 획득! 현재 개수: {mySnowballCount}"
                );
            }
        });
    }

    void HandleSnowItemRespawn(byte[] buffer)
    {
        int messageLength = buffer[1];

        string data =
            Encoding.UTF8.GetString(buffer, 2, messageLength);

        if (!int.TryParse(data, out int itemId))
            return;

        UnityMainThreadDispatcher.Enqueue(() =>
        {
            if (snowItems.TryGetValue(itemId, out SnowItem item))
            {
                item.gameObject.SetActive(true);
            }
        });
    }

    void RemoveOtherPlayer(byte[] buffer)
    {
        int messageLength = buffer[1];

        string data = Encoding.UTF8.GetString(buffer, 2, messageLength);

        if (!int.TryParse(data, out int disconnectedClientId))
            return;

        UnityMainThreadDispatcher.Enqueue(() =>
        {
            if (otherPlayers.TryGetValue(disconnectedClientId, out GameObject player))
            {
                Destroy(player);
                otherPlayers.Remove(disconnectedClientId);
            }
        });
    }

    void ClientColorFromServer(byte[] buffer)
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            byte messageLength = buffer[1];
            //위치 데이터 수신
            string data = Encoding.UTF8.GetString(buffer, 2, messageLength);
            Debug.Log("색, 이름 데이터 수신 : " + data);

            string[] parts = data.Split(':');
            int receivedClientId = int.Parse(parts[0]);
            int color = int.Parse(parts[1]);
            string name = parts[2];

            if (receivedClientId == clientId)
                return;

            if (!otherPlayers.ContainsKey(receivedClientId))
            {
                GameObject playerObject = Instantiate(Prefab[color]);
                otherPlayers[receivedClientId] = playerObject;
                otherPlayers[receivedClientId].GetComponent<PlayerState>().nameText.text = name;

                Rigidbody rb = playerObject.GetComponent<Rigidbody>();
                if(rb != null)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
            }
        });
    }

    void SetClientIdFromServer(byte[] buffer)
    {
        int messageLength = buffer[1];

        string data = Encoding.UTF8.GetString(buffer, 2, messageLength);

        string[] parts = data.Split(':', 2);

        clientId = int.Parse(parts[0]);
        sessionToken = parts[1];

        sessionReady = true;
        Debug.Log($"서버에서 ClientId 발급: {clientId}");



        byte[] sendData = Encoding.UTF8.GetBytes(
            ChoosePrefab
            + ":"
            + FindObjectOfType<LodingManager>().Name
         );

        byte[] packet = new byte[sendData.Length + 2];

        packet[0] = (byte)TcpPacketType.PlayerJoin;
        packet[1] = (byte)sendData.Length;

        Array.Copy(sendData, 0, packet, 2, sendData.Length);

        stream.Write(packet, 0, packet.Length);
    }

    void UpdateChatFromServer(byte[] buffer)
    {
        byte messageLength = buffer[1];
        //위치 데이터 수신
        string data = Encoding.UTF8.GetString(buffer, 2, messageLength);
        Debug.Log("채팅 데이터 수신: " + data);

        string[] parts = data.Split(':');
        int clientId = int.Parse(parts[0]);
        string chatMessage = parts[1];

        UnityMainThreadDispatcher.Enqueue(() =>
            {
                UpdateChat(chatMessage);
            });
    }

    void UpdateChat(string chat)
    {
        chat_text.text = chat;
    }

    void UpdatePositionFromServer(byte[] buffer)
    {
        byte messageLength = buffer[1];
        //위치 데이터 수신
        string data = Encoding.UTF8.GetString(buffer, 2, messageLength);
        

        string[] parts = data.Split(':');
        int clientId = int.Parse(parts[0]);
        int sequence = int.Parse(parts[1]);

        if (lastReceivedPositionSequence.TryGetValue(clientId, out int lastSequence))
        {
            if (sequence <= lastSequence)
                return;
        }

        Debug.Log("위치 데이터 수신: " + data);
        Debug.Log($"parts.Length = {parts.Length}");

        lastReceivedPositionSequence[clientId] = sequence;

        string[] position = parts[2].Split(',');

        float x = float.Parse(position[0]);
        float y = float.Parse(position[1]);
        float z = float.Parse(position[2]);

        float yaw = float.Parse(parts[3]);

        bool isMoving = parts[4] == "1";

        Vector3 newPosition = new Vector3(x, y, z);

        UnityMainThreadDispatcher.Enqueue(() =>
        {
            UpdateOtherClientPosition(clientId, newPosition, yaw, isMoving);
            text.text += ("위치 변경");
        });
    }

    void UpdateOtherClientPosition(int clientID, Vector3 newPosition, float yaw, bool isMoving)
    {
        if (otherPlayers.TryGetValue(clientID, out GameObject player))
        {
            targetPositions[clientID] = newPosition;
            targetYaws[clientID] = yaw;

            Animator animator = player.GetComponent<Animator>();

            if (animator != null)
                animator.SetBool("IsMoving", isMoving);
        }
    }

    private void UpdateRemotePlayers()
    {
        foreach (var pair in targetPositions)
        {
            int clientId = pair.Key;
            Vector3 targetPosition = pair.Value;

            if (!otherPlayers.TryGetValue(clientId, out GameObject player))
            {
                continue;
            }

            player.transform.position =
                Vector3.Lerp(
                    player.transform.position,
                    targetPosition,
                    10f * Time.deltaTime
                );


            if (targetYaws.TryGetValue(clientId, out float targetYaw))
            {
                Quaternion targetRotation = Quaternion.Euler(0f, targetYaw, 0f);

                player.transform.rotation = Quaternion.Slerp(
                    player.transform.rotation,
                    targetRotation,
                    10f * Time.deltaTime
                );
            }
        }
    }

    public void SetCharacter()
    {
        System.Random rand = new System.Random();
        int num = rand.Next(0, SpawnPos.Length);

        character = Instantiate(Prefab[ChoosePrefab], SpawnPos[num].position, Quaternion.identity);

        FindObjectOfType<CameraManager>().SetTarget(character.transform);
        SendPositionToServer(SpawnPos[num].position);

        character.GetComponent<PlayerState>().nameText.text = FindObjectOfType<LodingManager>().Name;
    }

    public void RequestSnowItem(int itemId)
    {
        byte[] data = Encoding.UTF8.GetBytes(itemId.ToString());

        byte[] packet = new byte[data.Length + 2];

        packet[0] = (byte)TcpPacketType.SnowItemRequest;
        packet[1] = (byte)data.Length;

        Array.Copy(data, 0, packet, 2, data.Length);

        stream.Write(packet, 0, packet.Length);

        Debug.Log($"SnowItem 획득 요청: {itemId}");
    }

    public void RequestThrowSnowball()
    {
        byte[] packet = new byte[2];

        packet[0] = (byte)TcpPacketType.SnowballThrowRequest;
        packet[1] = 0;

        stream.Write(packet, 0, packet.Length);
    }

    void HandleSnowballSpawn(byte[] buffer)
    {
        int messageLength = buffer[1];

        string data = Encoding.UTF8.GetString(buffer, 2, messageLength);

        string[] parts = data.Split(':');

        if (parts.Length != 5)
            return;

        if (!int.TryParse(parts[0], out int snowballId))
            return;

        if (!int.TryParse(parts[1], out int ownerId))
            return;

        string[] positionParts = parts[2].Split(',');
        string[] directionParts = parts[3].Split(',');

        if (positionParts.Length != 3 ||
            directionParts.Length != 3)
            return;

        if (!float.TryParse(positionParts[0], out float x) ||
            !float.TryParse(positionParts[1], out float y) ||
            !float.TryParse(positionParts[2], out float z) ||
            !float.TryParse(directionParts[0], out float dirX) ||
            !float.TryParse(directionParts[1], out float dirY) ||
            !float.TryParse(directionParts[2], out float dirZ) ||
            !float.TryParse(parts[4], out float speed))
        {
            return;
        }

        Vector3 position = new Vector3(x, y, z);
        Vector3 direction = new Vector3(dirX, dirY, dirZ);

        UnityMainThreadDispatcher.Enqueue(() =>
        {
            SpawnSnowball(
                snowballId,
                ownerId,
                position,
                direction,
                speed);
        });
    }

    void SpawnSnowball(
        int snowballId,
        int ownerId,
        Vector3 position,
        Vector3 direction,
        float speed)
    {
        if (snowballObjects.ContainsKey(snowballId))
            return;

        GameObject snowball =
            Instantiate(
                snowballPrefab,
                position,
                Quaternion.identity);

        snowballObjects[snowballId] = snowball;

        SnowballMovement movement = snowball.GetComponent<SnowballMovement>();

        movement.Initialize(
            direction,
            speed);



        Animator animator = null;

        if (ownerId == clientId)
        {
            animator = character.GetComponent<Animator>();
        }
        else if (otherPlayers.TryGetValue(ownerId, out GameObject ownerPlayer))
        {
            animator = ownerPlayer.GetComponent<Animator>();
        }

        if (animator != null)
        {
            animator.SetTrigger("Throw");
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if(tcpServer != null)
            tcpServer.Close();
        if(udpClient != null)
            udpClient.Close();
        text.text += "서버와 연결 종료" + "\n";
    }
}
