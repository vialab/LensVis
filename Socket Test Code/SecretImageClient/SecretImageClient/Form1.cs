using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;

namespace SecretImageClient
{
    public partial class Form1 : Form
    {

        TcpClient tcpclnt;
        TcpListener tcpSend;
        static Socket s;
        string ipAddressString = "10.10.123.191";

        Bitmap globalImg = null;

        Queue<MessageContent> messageQueue;
        private readonly SynchronizationContext synchronizationContext;

        public Form1()
        {
            InitializeComponent();
            synchronizationContext = SynchronizationContext.Current;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            messageQueue = new Queue<MessageContent>();
        }

        // Connect Button
        private void button1_Click(object sender, EventArgs e)
        {
            IPAddress ipAd = IPAddress.Parse(ipAddressString);
            tcpSend = new TcpListener(ipAd, 8002);
            tcpSend.Start();
            s = tcpSend.AcceptSocket();

            tcpclnt = new TcpClient();
            tcpclnt.Connect(ipAddressString, 8001);

            Task.Factory.StartNew(() =>
            {
                LoopForReceiving();
            });

            Task.Factory.StartNew(() =>
            {
                LoopForSending();
            });
        }

        // Request Image Button
        private void button2_Click(object sender, EventArgs e)
        {
            MessageContent newMessage = new MessageContent();
            newMessage.ContentType = 'R';
            messageQueue.Enqueue(newMessage);
        }

        bool LoopForReceiving()
        {
            try
            {

                Stream stm = tcpclnt.GetStream();
                while (true)
                {                    
                    byte[] messageTypeBytes = new byte[1];
                    char messageType;

                    stm.Read(messageTypeBytes, 0, 1);
                    messageType = Encoding.ASCII.GetChars(messageTypeBytes)[0];

                    if (messageType == 'I')
                    {
                        byte[] messageSizeBytes = new byte[4];
                        stm.Read(messageSizeBytes, 0, 4);
                        int imageSize = BitConverter.ToInt32(messageSizeBytes, 0);
                        try
                        {
                            byte[] image = new byte[imageSize];
                            stm.Read(image, 0, imageSize);

                            MemoryStream ms = new MemoryStream(image);
                            Image im = Image.FromStream(ms);

                            globalImg = new Bitmap(im);

                            updateImg(im);
                        }
                        catch
                        {

                        }
                    }
                    else if (messageType == 'C')
                    {
                        byte[] xBytes = new byte[4];
                        byte[] yBytes = new byte[4];
                        byte[] xCropBytes = new byte[4];
                        byte[] yCropBytes = new byte[4];

                        byte[] ml = new byte[4];

                        stm.Read(ml, 0, 4);

                        int totalMessageLength = 0;
                        totalMessageLength = BitConverter.ToInt32(ml, 0);

                        stm.Read(xBytes, 0, 4);
                        stm.Read(yBytes, 0, 4);
                        stm.Read(xCropBytes, 0, 4);
                        stm.Read(yCropBytes, 0, 4);
                        

                        int x = 0, y = 0, xCrop = 0, yCrop = 0;

                        
                        x = BitConverter.ToInt32(xBytes, 0);
                        y = BitConverter.ToInt32(yBytes, 0);
                        xCrop = BitConverter.ToInt32(xCropBytes, 0);
                        yCrop = BitConverter.ToInt32(yCropBytes, 0);
                        

                        Rectangle test = new Rectangle();
                        test.Height = yCrop;
                        test.Width = xCrop;
                        test.Location = new Point(x,y);

                        Image cropped = globalImg.Clone(test, globalImg.PixelFormat);

                        updateImg(cropped);

             

                    }

                }

            }
            catch (Exception e)
            {
                e = e;
            }

            return true;
        }

        async Task<bool> LoopForSending()
        {
            while (true)
            {
                if (messageQueue.Count > 0)
                {
                    MessageContent messageToSend = messageQueue.Dequeue();

                    s.Send(messageToSend.ContentTypeBytes);

                    if (messageToSend.MessageSize > 0)
                    {
                        s.Send(messageToSend.MessageSizeBytes);
                        s.Send(messageToSend.MessageBytes);
                    }

                }
            }

        }

        private void updateImg(Image img)
        {
            synchronizationContext.Post(new SendOrPostCallback(o =>
            {
                pictureBox1.Image = (Image)o;
                this.Refresh();
            }), img);
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }
    }
}
