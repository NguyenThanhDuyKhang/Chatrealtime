using System;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using ChatShared;

namespace ChatClient
{
    public partial class Form1 : Form
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private bool _isConnected = false;

        // [MỚI] Biến dùng cho tính năng Typing
        private System.Windows.Forms.Timer _typingTimer; // Timer để ẩn status sau vài giây
        private DateTime _lastTypingSent = DateTime.MinValue;

        public Form1()
        {
            InitializeComponent();
            
            // Khởi tạo Timer để ẩn status typing sau 3 giây không nhận được tín hiệu
            _typingTimer = new System.Windows.Forms.Timer();
            _typingTimer.Interval = 3000;
            _typingTimer.Tick += (s, e) => {
                lblStatus.Text = "";
                _typingTimer.Stop();
            };
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            string ip = txtIP.Text;
            string username = txtUsername.Text;
            
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Please enter a username.");
                return;
            }

            try 
            {
                _client = new TcpClient();
                _client.Connect(ip, 9999);
                _stream = _client.GetStream();
                _isConnected = true;

                Packet loginPacket = new Packet(PacketType.Login, username, "Login Request");
                SendPacket(loginPacket);

                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                pnlLogin.Visible = false;
                this.Text = $"Chat Client - {username}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}");
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (!_isConnected) return;
            string msg = txtMessage.Text;
            if (string.IsNullOrWhiteSpace(msg)) return;

            Packet packet = new Packet(PacketType.Message, txtUsername.Text, msg);
            SendPacket(packet);
            txtMessage.Clear();
        }

        // [MỚI] Sự kiện khi người dùng gõ phím
        private void txtMessage_TextChanged(object sender, EventArgs e)
        {
            if (!_isConnected) return;

            // Chỉ gửi thông báo "Đang gõ" mỗi 2 giây một lần để tránh Spam Server
            if ((DateTime.Now - _lastTypingSent).TotalMilliseconds > 2000)
            {
                Packet p = new Packet(PacketType.Typing, txtUsername.Text, "");
                SendPacket(p);
                _lastTypingSent = DateTime.Now;
            }
        }

        private void btnFile_Click(object sender, EventArgs e)
        {
            if (!_isConnected) return;

            OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                FileInfo fi = new FileInfo(ofd.FileName);
                if (fi.Length > 5 * 1024 * 1024)
                {
                    MessageBox.Show("File too large! Please send files under 5MB.");
                    return;
                }

                try
                {
                    byte[] fileBytes = File.ReadAllBytes(ofd.FileName);
                    Packet packet = new Packet
                    {
                        Type = PacketType.File,
                        Sender = txtUsername.Text,
                        FileName = fi.Name,
                        FileData = fileBytes
                    };
                    
                    SendPacket(packet);
                    AppendMessage($"You sent a file: {fi.Name}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading file: {ex.Message}");
                }
            }
        }

        private void SendPacket(Packet packet)
        {
            try
            {
                string json = JsonSerializer.Serialize(packet);
                byte[] contentBytes = Encoding.UTF8.GetBytes(json);
                byte[] lengthBytes = BitConverter.GetBytes(contentBytes.Length);

                lock(_stream)
                {
                    _stream.Write(lengthBytes, 0, 4);
                    _stream.Write(contentBytes, 0, contentBytes.Length);
                }
            }
            catch 
            {
                 // Ignore
            }
        }

        private void ReceiveLoop()
        {
            try
            {
                while (_isConnected)
                {
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = _stream.Read(lengthBuffer, 0, 4);
                    if (bytesRead == 0) break;

                    int packetLength = BitConverter.ToInt32(lengthBuffer, 0);

                    byte[] contentBuffer = new byte[packetLength];
                    int totalBytesReceived = 0;
                    while (totalBytesReceived < packetLength)
                    {
                        int received = _stream.Read(contentBuffer, totalBytesReceived, packetLength - totalBytesReceived);
                        if (received == 0) break;
                        totalBytesReceived += received;
                    }

                    string json = Encoding.UTF8.GetString(contentBuffer);
                    Packet packet = JsonSerializer.Deserialize<Packet>(json);

                    Invoke(new Action(() => HandlePacket(packet)));
                }
            }
            catch
            {
            }
            finally
            {
                Invoke(new Action(() => Disconnect()));
            }
        }

        private void HandlePacket(Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Message:
                    AppendMessage($"{packet.Sender}: {packet.Content}");
                    break;
                
                case PacketType.File:
                    AppendMessage($"{packet.Sender} sent a file: {packet.FileName}");
                    DialogResult dr = MessageBox.Show(
                        $"{packet.Sender} sent a file '{packet.FileName}'. Do you want to download it?", 
                        "File Received", 
                        MessageBoxButtons.YesNo, 
                        MessageBoxIcon.Question);
                    
                    if (dr == DialogResult.Yes)
                    {
                        SaveFileDialog sfd = new SaveFileDialog();
                        sfd.FileName = packet.FileName;
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            File.WriteAllBytes(sfd.FileName, packet.FileData);
                            MessageBox.Show("File saved successfully!");
                        }
                    }
                    break;

                // [MỚI] Xử lý hiển thị Typing
                case PacketType.Typing:
                    if (packet.Sender != txtUsername.Text) // Không hiện thông báo của chính mình
                    {
                        lblStatus.Text = $"{packet.Sender} is typing...";
                        _typingTimer.Stop();  // Reset timer
                        _typingTimer.Start(); // Bắt đầu đếm ngược 3 giây để ẩn
                    }
                    break;
            }
        }

        private void AppendMessage(string msg)
        {
            rtbMessages.AppendText($"[{DateTime.Now:HH:mm}] {msg}{Environment.NewLine}");
            rtbMessages.ScrollToCaret();
        }

        private void Disconnect()
        {
            if (!_isConnected) return; 

            _isConnected = false;
            _stream?.Close();
            _client?.Close();
            pnlLogin.Visible = true;
            MessageBox.Show("Disconnected from server.");
        }
    }
}