using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.IO;
using ChatShared;

namespace ChatServer
{
    public class ClientHandler
    {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private Form1 _serverForm;
        public string Username { get; private set; }

        public ClientHandler(TcpClient client, Form1 form)
        {
            _tcpClient = client;
            _serverForm = form;
            _stream = client.GetStream();
        }

        public void Process()
        {
            try
            {
                while (true)
                {
                    // 1. Đọc Header (4 bytes) để biết độ dài gói tin
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = _stream.Read(lengthBuffer, 0, 4);
                    if (bytesRead == 0) break; // Client đóng kết nối

                    int packetLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // 2. Đọc Content dựa trên độ dài
                    byte[] contentBuffer = new byte[packetLength];
                    int totalBytesReceived = 0;
                    while (totalBytesReceived < packetLength)
                    {
                        int received = _stream.Read(contentBuffer, totalBytesReceived, packetLength - totalBytesReceived);
                        if (received == 0) break;
                        totalBytesReceived += received;
                    }

                    string jsonString = Encoding.UTF8.GetString(contentBuffer);
                    
                    // 3. Deserialize và xử lý
                    Packet packet = JsonSerializer.Deserialize<Packet>(jsonString);
                    HandlePacket(packet);
                }
            }
            catch (Exception)
            {
                // Lỗi kết nối (Client tắt đột ngột)
            }
            finally
            {
                Close();
            }
        }
                private void HandlePacket(Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Login:
                    Username = packet.Sender;
                    _serverForm.Log($"User logged in: {Username}");
                    SendPacket(new Packet(PacketType.Message, "Server", $"Welcome {Username} to the chat! Type /help for commands."));
                    
                    // [MỚI] Gửi lại lịch sử chat cho người mới vào
                    var history = _serverForm.GetRecentHistory();
                    foreach (var oldPacket in history)
                    {
                        SendPacket(oldPacket);
                    }
                    break;

                case PacketType.Message:
                    _serverForm.Log($"{packet.Sender}: {packet.Content}");
                    _serverForm.BroadcastPacket(packet);

                    // [MỚI] CHAT BOT LOGIC
                    if (packet.Content.StartsWith("/"))
                    {
                        ProcessBotCommand(packet.Content);
                    }
                    break;

                case PacketType.File:
                    _serverForm.Log($"{packet.Sender} sent file: {packet.FileName} ({packet.FileData?.Length} bytes)");
                    _serverForm.BroadcastPacket(packet);
                    break;

                case PacketType.Typing:
                    // Server nhận được tin báo A đang gõ -> Broadcast cho mọi người biết (trừ A)
                    // Để đơn giản ta cứ broadcast hết, Client tự lọc
                     _serverForm.BroadcastPacket(packet);
                    break;
            }
        }
        
    }   
}     