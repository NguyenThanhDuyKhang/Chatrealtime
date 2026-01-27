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
    }   
}     