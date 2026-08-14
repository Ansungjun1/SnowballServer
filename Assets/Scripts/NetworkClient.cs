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

public class NetworkClient : MonoBehaviour
{
    private TcpClient tcpServer;
    private UdpClient udpClient;
    private NetworkStream stream;
    public TextMeshProUGUI text;
    public GameObject character;
    private Dictionary<int, GameObject> otherPlayers = new Dictionary<int, GameObject>();
    public GameObject[] Prefab;
    public GameObject myPrefab;
    public int ChoosePrefab;
    public TextMeshProUGUI chat_text;
    public TextMeshProUGUI my_Chat_text;
    public TMP_InputField input_Chat_text;
    public Transform[] SpawnPos;

    private bool isRunning;

    private readonly object lockObject = new object();  // lock을 위한 객체

    private int clientId = -1;
    private string sessionToken;
    private bool sessionReady = false;

    //private string ServerIP = "58.231.147.182";
    private string ServerIP = "192.168.0.5";
    private int udpPort = 9051;
    private int tcpPort = 9050;

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

    private void Update()
    {
        if (isRunning)
        {
            if (character != null)
            {
                if (Input.GetKey(KeyCode.W)) HandlePlayerMovement(KeyCode.W);
                else if (Input.GetKeyUp(KeyCode.W)) character.GetComponent<PlayerMovement>().MovePlayer("W");
                if (Input.GetKey(KeyCode.D)) HandlePlayerMovement(KeyCode.D);
                else if (Input.GetKeyUp(KeyCode.D)) character.GetComponent<PlayerMovement>().MovePlayer("A");
                if (Input.GetKey(KeyCode.S)) HandlePlayerMovement(KeyCode.S);
                else if (Input.GetKeyUp(KeyCode.S)) character.GetComponent<PlayerMovement>().MovePlayer("W");
                if (Input.GetKey(KeyCode.A)) HandlePlayerMovement(KeyCode.A);
                else if (Input.GetKeyUp(KeyCode.A)) character.GetComponent<PlayerMovement>().MovePlayer("A");

                character.GetComponent<PlayerMovement>().SetMovePlayer();

                if (Input.GetKeyDown(KeyCode.Return))
                    HandlePlayerChat();
            }
        }
    }

    private void HandlePlayerMovement(KeyCode key)
    {
        character.GetComponent<PlayerMovement>().MovePlayer(key);

        Vector3 position = character.transform.position;
        string positionData = sessionToken + ":" + position.x + "," + position.y + "," + position.z;
        byte[] data = Encoding.UTF8.GetBytes(positionData);
        byte[] packet = new byte[data.Length + 2];
        packet[0] = (byte)TcpPacketType.PlayerJoin;
        packet[1] = (byte)data.Length;
        Array.Copy(data, 0, packet, 2, data.Length);

        SendPositionToServer(packet);
    }

    void SendPositionToServer(byte[] data)
    {
        if (!sessionReady) return;

        udpClient.Send(data, data.Length);

        text.text += "서버로 내 위치 전송" + "\n";
    }

    public void HandlePlayerChat()
    {
        if (string.IsNullOrEmpty(input_Chat_text.text))
            return;

        byte[] data = Encoding.UTF8.GetBytes(input_Chat_text.text);

        if (data.Length > byte.MaxValue)
        {
            Debug.Log("채팅 데이터가 너무 깁니다.");
            return;
        }

        my_Chat_text.text = input_Chat_text.text;
        input_Chat_text.text = "";

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

            default:
                Debug.Log("알 수 없는 패킷 타입:" + packetType);
                break;
        }
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
                    case 0x02:
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

    void UpdatePositionFromServer(byte[] buffer)
    {
        byte messageLength = buffer[1];
        //위치 데이터 수신
        string data = Encoding.UTF8.GetString(buffer, 2, messageLength);
        //Debug.Log("위치 데이터 수신: " + data);

        string[] parts = data.Split(':');
        int clientId = int.Parse(parts[0]);

        string[] position = parts[1].Split(',');
        //Debug.Log(position[0] +"," + position[1] +","+ position[2]);
        float x = float.Parse(position[0]);
        float y = float.Parse(position[1]);
        float z = float.Parse(position[2]);

        Vector3 newPosition = new Vector3(x, y, z);

        UnityMainThreadDispatcher.Enqueue(() =>
        {
            UpdateOtherClientPosition(clientId, newPosition);
            text.text += ("위치 변경");
        });
    }

    void UpdateOtherClientPosition(int clientID, Vector3 newPosition)
    {
        if (otherPlayers.ContainsKey(clientID))
            otherPlayers[clientID].transform.position = newPosition;
    }

    public void SetCharacter()
    {
        character = Instantiate(Prefab[ChoosePrefab]);
        System.Random rand = new System.Random();
        character.transform.position = SpawnPos[rand.Next(0,SpawnPos.Length)].position;
        FindObjectOfType<CameraManager>().SetPosition(character.transform);
        HandlePlayerMovement(KeyCode.Escape);
        character.GetComponent<PlayerState>().nameText.text = FindObjectOfType<LodingManager>().Name;
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
