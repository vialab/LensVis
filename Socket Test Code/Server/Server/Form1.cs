using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Server.Properties;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using System.Linq;
using System.IO;

namespace Server
{
  public partial class Form1 : Form
  {
    static Socket s;
    System.Drawing.Graphics graphics;
    static Bitmap globalImage = Resources.GlobalImage;
    static Bitmap secretImage = Resources.SecretImage;
    Queue<MessageContent> messageQueue;

    string ipAddressString = "10.10.123.191";

    private readonly SynchronizationContext synchronizationContext;

    public Form1()
    {
      InitializeComponent();
      synchronizationContext = SynchronizationContext.Current;
      messageQueue = new Queue<MessageContent>();
    }

    private void button1_Click(object sender, EventArgs e)
    {
      byte[] test;
      test = BitConverter.GetBytes('R');

      graphics = this.CreateGraphics();

      Task.Factory.StartNew(() =>
      {
        Send();
      });

      Task.Factory.StartNew(() =>
      {
        Recieve();
      });

    }

    private async Task rsend()
    {
      Random rnd = new Random();
      while (true)
      {
        MessageContent nm = new MessageContent();
        nm.ContentType = 'C';
        int x = 300 + rnd.Next(-50,50), y = 300 + rnd.Next(-50, 50), xCrop = 250, yCrop = 200;

        byte[] xb = (BitConverter.GetBytes(x));
        byte[] yb = (BitConverter.GetBytes(y));
        byte[] xCropb = (BitConverter.GetBytes(xCrop));
        byte[] yCropb = (BitConverter.GetBytes(yCrop));

        byte[] concatArr = new byte[xb.Length + yb.Length + xCropb.Length + yCropb.Length];
        System.Buffer.BlockCopy(xb, 0, concatArr, 0, xb.Length);
        System.Buffer.BlockCopy(yb, 0, concatArr, xb.Length, yb.Length);
        System.Buffer.BlockCopy(xCropb, 0, concatArr, xb.Length + yb.Length, xCropb.Length);
        System.Buffer.BlockCopy(yCropb, 0, concatArr, xb.Length + yb.Length + xCropb.Length, yCropb.Length);

        nm.MessageBytes = concatArr;

        messageQueue.Enqueue(nm);
        System.Threading.Thread.Sleep(20);
      }
    }

    private async Task Send()
    {
      int i = 0;
      Bitmap img = null;
      ImageConverter converter = new ImageConverter();

      try
      {
        IPAddress ipAd = IPAddress.Parse(ipAddressString);
        // use local m/c IP address, and 
        // use the same in the client

        /* Initializes the Listener */
        TcpListener myList = new TcpListener(ipAd, 8001);

        /* Start Listeneting at the specified port */
        myList.Start();

        updateRTB("The server is running at port 8001...\n");
        updateRTB("The local End point is  :" + myList.LocalEndpoint + "\n");
        updateRTB("Waiting for a connection.....\n");
        s = myList.AcceptSocket();
        updateRTB("Connection accepted from " + s.RemoteEndPoint + "\n");

        i = 0;
        img = globalImage;
      }
      catch (Exception e)
      {
        e = e;
      }


      while (true)
      {
        try
        {
          if (messageQueue.Count > 0)
          {
            MessageContent messageToSend = messageQueue.Dequeue();
            s.Send(messageToSend.ContentTypeBytes);
            s.Send(messageToSend.MessageSizeBytes);
            s.Send(messageToSend.MessageBytes);
          }
        }
        catch (Exception e)
        {
          e = e;
        }


      }
    }


    private async Task Recieve()
    {
      int flag = 0;

      Stream stm = null;

      updateRTB("Connecting... \n");

      while (flag == 0)
      {
        try
        {
          IPAddress ipAd = IPAddress.Parse(ipAddressString);
          // use local m/c IP address, and 
          // use the same in the client

          /* Initializes the Listener */
          TcpClient tcpclnt = new TcpClient();

          /* Start Listeneting at the specified port */
          tcpclnt.Connect(ipAddressString, 8002);

          stm = tcpclnt.GetStream();
          flag = 1;
        }
        catch (Exception e)
        {
          e = e;
        }
      }

      updateRTB("Connection Established\n");

      while (true)
      {
        byte[] messageSizeBytes = new byte[4];
        byte[] messageTypeBytes = new byte[1];
        char messageType;
        ImageConverter converter = new ImageConverter();

        stm.Read(messageTypeBytes, 0, 1);

        messageType = Encoding.ASCII.GetChars(messageTypeBytes)[0];

        updateRTB(messageType.ToString());

        MessageContent newMessage = new MessageContent();

        if (messageType == 'R')
        {

          newMessage.ContentType = 'I';
          newMessage.MessageBytes = (byte[])converter.ConvertTo(Resources.SecretImage, typeof(byte[]));

          messageQueue.Enqueue(newMessage);

          //Task.Factory.StartNew(() =>
          //{
          //  rsend();
          //});
        }
        else if (messageType == 'P')
        {
          //newMessage.ContentType = 'T';

        }


      }
    }


    private void updateRTB(string text)
    {
      synchronizationContext.Post(new SendOrPostCallback(o =>
      {
        richTextBox1.AppendText((string)o + "\n");
        this.Refresh();
      }), text);
    }

    private void Form1_Load(object sender, EventArgs e)
    {

    }

    private void button2_Click(object sender, EventArgs e)
    {
      MessageContent nm = new MessageContent();
      nm.ContentType = 'C';
      int x = 300, y = 300, xCrop = 250, yCrop = 200;

      byte[] xb = (BitConverter.GetBytes(x));
      byte[] yb = (BitConverter.GetBytes(y));
      byte[] xCropb = (BitConverter.GetBytes(xCrop));
      byte[] yCropb = (BitConverter.GetBytes(yCrop));

      byte[] concatArr = new byte[xb.Length + yb.Length + xCropb.Length + yCropb.Length];
      System.Buffer.BlockCopy(xb, 0, concatArr, 0, xb.Length);
      System.Buffer.BlockCopy(yb, 0, concatArr, xb.Length, yb.Length);
      System.Buffer.BlockCopy(xCropb, 0, concatArr, xb.Length + yb.Length, xCropb.Length);
      System.Buffer.BlockCopy(yCropb, 0, concatArr, xb.Length + yb.Length + xCropb.Length, yCropb.Length);

      nm.MessageBytes = concatArr;

      messageQueue.Enqueue(nm);
    }
  }
}
