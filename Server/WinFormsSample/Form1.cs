//=============================================================================
// Copyright © 2009 NaturalPoint, Inc. All Rights Reserved.
// 
// This software is provided by the copyright holders and contributors "as is" and
// any express or implied warranties, including, but not limited to, the implied
// warranties of merchantability and fitness for a particular purpose are disclaimed.
// In no event shall NaturalPoint, Inc. or contributors be liable for any direct,
// indirect, incidental, special, exemplary, or consequential damages
// (including, but not limited to, procurement of substitute goods or services;
// loss of use, data, or profits; or business interruption) however caused
// and on any theory of liability, whether in contract, strict liability,
// or tort (including negligence or otherwise) arising in any way out of
// the use of this software, even if advised of the possibility of such damage.
//=============================================================================

using System;
using System.IO;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using System.Linq;

using NatNetML;
using System.Windows.Media.Media3D;
using Microsoft.Xna.Framework;
using WinFormTestApp.Properties;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;

/*
*
* Simple C# .NET sample showing how to use the NatNet managed assembly (NatNETML.dll).
* 
* It is designed to illustrate using NatNet.  There are some inefficiencies to keep the
* code as simple to read as possible.
* 
* Sections marked with a [NatNet] are NatNet related and should be implemented in your code.
* 
* This sample uses the Microsoft Chart Controls for Microsoft .NET for graphing, which
* requires the following assemblies:
*   - System.Windows.Forms.DataVisualization.Design.dll
*   - System.Windows.Forms.DataVisualization.dll
* Make sure you have these in your path when building and redistributing.
* 
*/

namespace WinFormTestApp
{
    public partial class Form1 : Form
    {
        // [NatNet] Our NatNet object
        private NatNetML.NatNetClientML m_NatNet;

        // [NatNet] Our NatNet Frame of Data object
        private NatNetML.FrameOfMocapData m_FrameOfData = new NatNetML.FrameOfMocapData();

        // [NatNet] Description of the Active Model List from the server (e.g. Motive)
        NatNetML.ServerDescription desc = new NatNetML.ServerDescription();

        // [NatNet] Queue holding our incoming mocap frames the NatNet server (e.g. Motive)
        private Queue<NatNetML.FrameOfMocapData> m_FrameQueue = new Queue<NatNetML.FrameOfMocapData>();

        // spreadsheet lookup
        Hashtable htMarkers = new Hashtable();
        Hashtable htRigidBodies = new Hashtable();
        List<RigidBody> mRigidBodies = new List<RigidBody>();

        Hashtable htForcePlates = new Hashtable();
        List<ForcePlate> mForcePlates = new List<ForcePlate>();

        // graphing support
        const int GraphFrames = 500;
        int m_iLastFrameNumber = 0;

        // frame timing information
        double m_fLastFrameTimestamp = 0.0f;
        float m_fCurrentMocapFrameTimestamp = 0.0f;
        float m_fFirstMocapFrameTimestamp = 0.0f;
        QueryPerfCounter m_FramePeriodTimer = new QueryPerfCounter();
        QueryPerfCounter m_UIUpdateTimer = new QueryPerfCounter();

        // server information
        double m_ServerFramerate = 1.0f;
        float m_ServerToMillimeters = 1.0f;
        int m_UpAxis = 1;   // 0=x, 1=y, 2=z (Y default)
        int mAnalogSamplesPerMocpaFrame = 0;
        int mDroppedFrames = 0;
        int mLastFrame = 0;

        private static object syncLock = new object();
        private delegate void OutputMessageCallback(string strMessage);
        private bool needMarkerListUpdate = false;
        private bool mPaused = false;

        // UI updating
        delegate void UpdateUICallback();
        bool mApplicationRunning = true;

        // polling
        delegate void PollCallback();
        Thread pollThread;
        bool mPolling = false;

        bool mRecording = false;
        TextWriter mWriter;

        NatNetML.RigidBodyData phone = null;
        NatNetML.RigidBodyData wall = null;

        Point3D topLeft;
        Point3D topRight;
        Point3D bottomLeft;
        Point3D bottomRight;

        Point3D pTopLeft;
        Point3D pTopRight;
        Point3D pBottomRight;
        Point3D pBottomLeft;
        
        Plane wallPlane;    

        int xScale = 1280*2;
        int yScale = 800;

        double xSize = 0;
        double ySize = 0;

        PointF[] pastPoints = new PointF[4];

        int count = 0;
        Socket s;

        #region stuff
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

            // Show available ip addresses of this machine
            String strMachineName = Dns.GetHostName();

            // create NatNet client
            int iConnectionType = 0;

                //iConnectionType = 1;
            int iResult = CreateClient(iConnectionType);

            // create and run an Update UI thread
            UpdateUICallback d = new UpdateUICallback(UpdateUI);
            var thread = new Thread(() =>
            {
                while (mApplicationRunning)
                {
                    try
                    {
                        this.Invoke(d);
                        Thread.Sleep(15);
                    }
                    catch (System.Exception ex)
                    {
                        break;
                    }
                }
            });
            thread.Start();

            // create and run a polling thread
            PollCallback pd = new PollCallback(PollData);
            pollThread = new Thread(() =>
            {
                while (mPolling)
                {
                    try
                    {
                        this.Invoke(pd);
                        Thread.Sleep(15);
                    }
                    catch (System.Exception ex)
                    {
                        break;
                    }
                }
            });

        }

        /// <summary>
        /// Create a new NatNet client, which manages all communication with the NatNet server (e.g. Motive)
        /// </summary>
        /// <param name="iConnectionType">0 = Multicast, 1 = Unicast</param>
        /// <returns></returns>
        private int CreateClient(int iConnectionType)
        {
            // release any previous instance
            if (m_NatNet != null)
            {
                m_NatNet.Uninitialize();
            }

            // [NatNet] create a new NatNet instance
            m_NatNet = new NatNetML.NatNetClientML(iConnectionType);

            // [NatNet] set a "Frame Ready" callback function (event handler) handler that will be
            // called by NatNet when NatNet receives a frame of data from the server application
            m_NatNet.OnFrameReady += new NatNetML.FrameReadyEventHandler(m_NatNet_OnFrameReady);

            /*
            // [NatNet] for testing only - event signature format required by some types of .NET applications (e.g. MatLab)
            m_NatNet.OnFrameReady2 += new FrameReadyEventHandler2(m_NatNet_OnFrameReady2);
            */

            // [NatNet] print version info
            int[] ver = new int[4];
            ver = m_NatNet.NatNetVersion();
            String strVersion = String.Format("NatNet Version : {0}.{1}.{2}.{3}", ver[0], ver[1], ver[2], ver[3]);

            return 0;
        }

        /// <summary>
        /// Connect to a NatNet server (e.g. Motive)
        /// </summary>
        private void Connect()
        {

            IPAddress ipAd = IPAddress.Parse("172.24.13.160");
            // use local m/c IP address, and 
            // use the same in the client

            /* Initializes the Listener */
            TcpListener myList = new TcpListener(ipAd, 8002);

            /* Start Listeneting at the specified port */
            myList.Start();

            Console.WriteLine("The server is running at port 8002...");
            Console.WriteLine("The local End point is  :" +
                              myList.LocalEndpoint);
            Console.WriteLine("Waiting for a connection.....");

            s = myList.AcceptSocket();
            Console.WriteLine("Connection accepted from " + s.RemoteEndPoint);


            // [NatNet] connect to a NatNet server
            int returnCode = 0;
            string strLocalIP = "127.0.0.1";
            string strServerIP = "127.0.0.1";
            returnCode = m_NatNet.Initialize(strLocalIP, strServerIP);


            // [NatNet] validate the connection
            returnCode = m_NatNet.GetServerDescription(desc);

        }

        private void Disconnect()
        {
            // [NatNet] disconnect
            // optional : for unicast clients only - notify Motive we are disconnecting
            int nBytes = 0;
            byte[] response = new byte[10000];
            int rc;
            rc = m_NatNet.SendMessageAndWait("Disconnect", out response, out nBytes);
            if (rc == 0)
            {

            }
            // shutdown our client socket
            m_NatNet.Uninitialize();
            checkBoxConnect.Text = "Connect";
        }

        private void checkBoxConnect_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxConnect.Checked)
            {
                Connect();

                mForcePlates.Clear();
                htForcePlates.Clear();
                mRigidBodies.Clear();
                htMarkers.Clear();
                htRigidBodies.Clear();
                needMarkerListUpdate = true;

                List<NatNetML.DataDescriptor> descs = new List<NatNetML.DataDescriptor>();
                bool bSuccess = m_NatNet.GetDataDescriptions(out descs);
                if (bSuccess)
                {
                    int iObject = 0;
                    foreach (NatNetML.DataDescriptor d in descs)
                    {
                        iObject++;

                        // MarkerSets
                        if (d.type == (int)NatNetML.DataDescriptorType.eMarkerSetData)
                        {
                            NatNetML.MarkerSet ms = (NatNetML.MarkerSet)d;

                            for (int i = 0; i < ms.nMarkers; i++)
                            {
                                String strUniqueName = ms.Name + i.ToString();
                                int key = strUniqueName.GetHashCode();
                            }
                        }
                        // RigidBodies
                        else if (d.type == (int)NatNetML.DataDescriptorType.eRigidbodyData)
                        {
                            NatNetML.RigidBody rb = (NatNetML.RigidBody)d;
                            mRigidBodies.Add(rb);


                        }
                        // Skeletons
                        else if (d.type == (int)NatNetML.DataDescriptorType.eSkeletonData)
                        {
                            NatNetML.Skeleton sk = (NatNetML.Skeleton)d;
                            for (int i = 0; i < sk.nRigidBodies; i++)
                            {
                                RigidBody rb = sk.RigidBodies[i];
                            }
                        }
                        // ForcePlates
                        else if (d.type == (int)NatNetML.DataDescriptorType.eForcePlateData)
                        {
                            NatNetML.ForcePlate fp = (NatNetML.ForcePlate)d;
                            mForcePlates.Add(fp);
                            // ForcePlateIDToRow map
                            int key = fp.ID.GetHashCode();
                        }

                        else
                        {
                            //OutputMessage("Unknown DataType");
                        }
                    }
                }
                else
                {
                    //OutputMessage("Unable to retrieve DataDescriptions");
                }
            }
            else
            {
                Disconnect();
            }
        }

        private RigidBody FindRB(int id)
        {
            foreach (RigidBody rb in mRigidBodies)
            {
                if (rb.ID == id)
                    return rb;
            }
            return null;
        }
        
        /// <summary>
        /// [NatNet] Request a description of the Active Model List from the server (e.g. Motive) and build up a new spreadsheet  
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonGetDataDescriptions_Click(object sender, EventArgs e)
        {

        }

        void ProcessFrameOfData(ref NatNetML.FrameOfMocapData data)
        {
            // detect and reported any 'reported' frame drop (as reported by server)
            if (m_fLastFrameTimestamp != 0.0f)
            {
                double framePeriod = 1.0f / m_ServerFramerate;
                double thisPeriod = data.fTimestamp - m_fLastFrameTimestamp;
                double fudgeFactor = 0.002f; // 2 ms
                if ((thisPeriod - framePeriod) > fudgeFactor)
                {
                    //OutputMessage("Frame Drop: ( ThisTS: " + data.fTimestamp.ToString("F3") + "  LastTS: " + m_fLastFrameTimestamp.ToString("F3") + " )");
                    mDroppedFrames++;
                }
            }

            // check and report frame drop (frame id based)
            if (mLastFrame != 0)
            {
                if ((data.iFrame - mLastFrame) != 1)
                {
                    //OutputMessage("Frame Drop: ( ThisFrame: " + data.iFrame.ToString() + "  LastFrame: " + mLastFrame.ToString() + " )");
                    //mDroppedFrames++;
                }
            }

            // recording : write packet to data file
            if (mRecording)
            {
                WriteFrame(data);
            }

            // [NatNet] Add the incoming frame of mocap data to our frame queue,  
            // Note: the frame queue is a shared resource with the UI thread, so lock it while writing
            lock (syncLock)
            {
                // [optional] clear the frame queue before adding a new frame
                m_FrameQueue.Clear();
                FrameOfMocapData deepCopy = new FrameOfMocapData(data);
                m_FrameQueue.Enqueue(deepCopy);
            }

            mLastFrame = data.iFrame;
            m_fLastFrameTimestamp = data.fTimestamp;

        }

        /// <summary>
        /// [NatNet] m_NatNet_OnFrameReady will be called when a frame of Mocap
        /// data has is received from the server application.
        ///
        /// Note: This callback is on the network service thread, so it is
        /// important to return from this function quickly as possible 
        /// to prevent incoming frames of data from buffering up on the
        /// network socket.
        ///
        /// Note: "data" is a reference structure to the current frame of data.
        /// NatNet re-uses this same instance for each incoming frame, so it should
        /// not be kept (the values contained in "data" will become replaced after
        /// this callback function has exited).
        /// </summary>
        /// <param name="data">The actual frame of mocap data</param>
        /// <param name="client">The NatNet client instance</param>
        void m_NatNet_OnFrameReady(NatNetML.FrameOfMocapData data, NatNetML.NatNetClientML client)
        {
            double elapsedIntraMS = 0.0f;
            QueryPerfCounter intraTimer = new QueryPerfCounter();
            intraTimer.Start();

            // detect and report and 'measured' frame drop (as measured by client)
            m_FramePeriodTimer.Stop();
            double elapsedMS = m_FramePeriodTimer.Duration();

            ProcessFrameOfData(ref data);

            // report if we are taking too long, which blocks packet receiving, which if long enough would result in socket buffer drop
            intraTimer.Stop();
            elapsedIntraMS = intraTimer.Duration();
            if (elapsedIntraMS > 5.0f)
            {
                //OutputMessage("Warning : Frame handler taking too long: " + elapsedIntraMS.ToString("F2"));
            }

            m_FramePeriodTimer.Start();

        }

        // [NatNet] [optional] alternate function signatured frame ready callback handler for .NET applications/hosts
        // that don't support the m_NatNet_OnFrameReady defined above (e.g. MATLAB)
        void m_NatNet_OnFrameReady2(object sender, NatNetEventArgs e)
        {
            m_NatNet_OnFrameReady(e.data, e.client);
        }

        private void PollData()
        {
            FrameOfMocapData data = m_NatNet.GetLastFrameOfData();
            ProcessFrameOfData(ref data);
        }

        private void SetDataPolling(bool poll)
        {
            if (poll)
            {
                // disable event based data handling
                m_NatNet.OnFrameReady -= m_NatNet_OnFrameReady;

                // enable polling 
                mPolling = true;
                pollThread.Start();
            }
            else
            {
                // disable polling
                mPolling = false;

                // enable event based data handling
                m_NatNet.OnFrameReady += new NatNetML.FrameReadyEventHandler(m_NatNet_OnFrameReady);
            }
        }

        private void GetLastFrameOfData()
        {
            FrameOfMocapData data = m_NatNet.GetLastFrameOfData();
            ProcessFrameOfData(ref data);
        }

        private void GetLastFrameOfDataButton_Click(object sender, EventArgs e)
        {
            // [NatNet] GetLastFrameOfData can be used to poll for the most recent avail frame of mocap data.
            // This mechanism is slower than the event handler mechanism, and in general is not recommended,
            // since it must wait for a frame to become available and apply a lock to that frame while it copies
            // the data to the returned value.

            // get a copy of the most recent frame of data
            // returns null if not available or cannot obtain a lock on it within a specified timeout
            FrameOfMocapData data = m_NatNet.GetLastFrameOfData();
            if (data != null)
            {
                // do something with the data
                String frameInfo = String.Format("FrameID : {0}", data.iFrame);
                //OutputMessage(frameInfo);
            }
        }

        private void WriteFrame(FrameOfMocapData data)
        {
            String str = "";

            str += data.fTimestamp.ToString("F3") + "\t";

            // 'all' markerset data
            for (int i = 0; i < m_FrameOfData.nMarkerSets; i++)
            {
                NatNetML.MarkerSetData ms = m_FrameOfData.MarkerSets[i];
                if (ms.MarkerSetName == "all")
                {
                    for (int j = 0; j < ms.nMarkers; j++)
                    {
                        str += ms.Markers[j].x.ToString("F3") + "\t";
                        str += ms.Markers[j].y.ToString("F3") + "\t";
                        str += ms.Markers[j].z.ToString("F3") + "\t";
                    }
                }
            }

            // force plates
            // just write first subframe from each channel (fx[0], fy[0], fz[0], mx[0], my[0], mz[0])
            for (int i = 0; i < m_FrameOfData.nForcePlates; i++)
            {
                NatNetML.ForcePlateData fp = m_FrameOfData.ForcePlates[i];
                for (int iChannel = 0; iChannel < fp.nChannels; iChannel++)
                {
                    if (fp.ChannelData[iChannel].nFrames == 0)
                    {
                        str += 0.0f;    // empty frame
                    }
                    else
                    {
                        str += fp.ChannelData[iChannel].Values[0] + "\t";
                    }
                }
            }

            mWriter.WriteLine(str);
        }
        #endregion

        private float findDist(Point3D p1, Point3D p2)
        {
            float dist = (float) Math.Sqrt(Math.Pow((p2.X - p1.X),2) + Math.Pow((p2.Y - p1.Y),2) + Math.Pow((p2.Z - p1.Z),2));
            return dist;
        }

        private void UpdateUI()
        {
            m_UIUpdateTimer.Stop();
            double interframeDuration = m_UIUpdateTimer.Duration();

            QueryPerfCounter uiIntraFrameTimer = new QueryPerfCounter();
            uiIntraFrameTimer.Start();

            // the frame queue is a shared resource with the FrameOfMocap delivery thread, so lock it while reading
            // note this can block the frame delivery thread.  In a production application frame queue management would be optimized.
            lock (syncLock)
            {
                while (m_FrameQueue.Count > 0)
                {
                    m_FrameOfData = m_FrameQueue.Dequeue();

                    if (m_FrameQueue.Count > 0)
                        continue;

                    if (m_FrameOfData != null)
                    {
                        // for servers that only use timestamps, not frame numbers, calculate a 
                        // frame number from the time delta between frames
                        if (desc.HostApp.Contains("TrackingTools"))
                        {
                            m_fCurrentMocapFrameTimestamp = m_FrameOfData.fLatency;
                            if (m_fCurrentMocapFrameTimestamp == m_fLastFrameTimestamp)
                            {
                                continue;
                            }
                            if (m_fFirstMocapFrameTimestamp == 0.0f)
                            {
                                m_fFirstMocapFrameTimestamp = m_fCurrentMocapFrameTimestamp;
                            }
                            m_FrameOfData.iFrame = (int)((m_fCurrentMocapFrameTimestamp - m_fFirstMocapFrameTimestamp) * m_ServerFramerate);

                        }


                        // update RigidBody data
                        for (int i = 0; i < m_FrameOfData.nRigidBodies; i++)
                        {
                            NatNetML.RigidBodyData rb = m_FrameOfData.RigidBodies[i];
                            //int key = rb.ID.GetHashCode();

                            RigidBody rbDef = FindRB(rb.ID);

                            if (rbDef != null)
                            {
                                if (rbDef.Name == "Phone")
                                {
                                    if (wall != null)
                                    {
                                        phone = rb;

                                        Point3D avg = new Point3D();
                                        avg.X = (phone.Markers[0].x + phone.Markers[1].x + phone.Markers[2].x) / 3;
                                        avg.Y = (phone.Markers[0].y + phone.Markers[1].y + phone.Markers[2].y) / 3;
                                        avg.Z = (phone.Markers[0].z + phone.Markers[1].z + phone.Markers[2].z) / 3;

                                        System.Drawing.Graphics graphics = this.CreateGraphics();

                                        Bitmap globalImage = Resources.GlobalImage;
                                        Bitmap secretImage = Resources.SecretImage;

                                        graphics.DrawImageUnscaled(globalImage,0,0);

                                        float tltr = findDist(topLeft, topRight);
                                        float tlbl = findDist(topLeft, bottomLeft);

                                        // each pixel = ~0.00394 x 0.00152 x 0.00273 space
                                        //int zScale = 700;

                                        float p2bZDist = (float)Math.Abs(avg.Z - ((topLeft.Z + bottomRight.Z) / 2));


                                        Point3D adjPoint = new Point3D();
                                        adjPoint.X = ((topLeft.X - avg.X) * 710) - 20; // Unfortunately, the points on the wall don't line up with the projector
                                        adjPoint.Y = ((topLeft.Y - avg.Y) * 725) - 50; // Therefore, we have to adjust it manually
                                        adjPoint.Z = topLeft.Z - avg.Z;        

                                        label1.Text = "X: " + adjPoint.X.ToString();
                                        label2.Text = "Y: " + adjPoint.Y.ToString();
                                        label3.Text = "Z: " + adjPoint.Z.ToString();

                                        System.Drawing.Rectangle pRect = new System.Drawing.Rectangle();
                                        pRect.Height = (int)Math.Round(9 * (1 + 12 * adjPoint.Z));
                                        pRect.Width = (int)Math.Round(16 * (1 + 12 * adjPoint.Z));
                                        pRect.Location = new System.Drawing.Point((int)Math.Round(adjPoint.X - pRect.Width / 2), (int)Math.Round(adjPoint.Y - pRect.Height / 2));

                                        Image cropped;

                                        pRect.Location = new System.Drawing.Point(500,300);


                                        cropped = secretImage.Clone(pRect, secretImage.PixelFormat);
                                        ImageConverter converter = new ImageConverter();
                                        //byte[] barr = (byte[])converter.ConvertTo(cropped, typeof(byte[]));

                                        MemoryStream ms = new MemoryStream();
                                        cropped.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);

                                        int test = ms.ToArray().Length;
                                        byte[] intBytes = BitConverter.GetBytes(ms.ToArray().Length);
                                        /*byte[] intBytes = new byte[5];

                                        intBytes[0] = 127;
                                        intBytes[1] = intBytesPre[0];
                                        intBytes[2] = intBytesPre[1];
                                        intBytes[3] = intBytesPre[2];
                                        intBytes[4] = intBytesPre[3]; */

                                        pictureBox2.Image = Image.FromStream(ms);
                                        this.Refresh();
                                                                                                                
                                        byte[] sdf = ms.ToArray();

                                        s.Send(intBytes);
                                        s.Send(ms.ToArray());

                                        var md5 = MD5.Create();
                                        byte[] cs = md5.ComputeHash(ms);
                                        byte[] csl = BitConverter.GetBytes(cs.Length);                         

                                        StringBuilder result = new StringBuilder(cs.Length * 2);

                                        for (int u = 0; u < cs.Length; u++)
                                            result.Append(cs[u].ToString());

                                        String resSt = result.ToString();                                        

                                        s.Send(csl);
                                        s.Send(cs);


                                        int ksdf = 4;
                                        ksdf++;


                                        //if (pRect.Location.X > -250 && pRect.Location.X < 2560+250)
                                        //{
                                        //    if (pRect.Location.Y > -250 && pRect.Location.Y < 800+250)
                                        //    {
                                        //        if (pRect.Location.Y + pRect.Height < 800+250)
                                        //        {
                                        //            if (pRect.Location.X + pRect.Width < 2560+250)
                                        //            {
                                        //                pRect.X += 250;
                                        //                pRect.Y += 250;
                                        //                cropped = secretImage.Clone(pRect, secretImage.PixelFormat);

                                        //                pictureBox1.Image = cropped;

                                        //                //if (count % 10 == 0)
                                        //                //{
                                        //                    ImageConverter converter = new ImageConverter();
                                        //                    byte[] barr = (byte[])converter.ConvertTo(cropped, typeof(byte[]));

                                        //                    byte[] intBytes = BitConverter.GetBytes(barr.Length);
                                        //                    s.Send(intBytes);
                                        //                    s.Send(barr);                                                        
                                        //                //}

                                        //                count++;



                                        //                //Thread.Sleep(50);

                                        //            }
                                        //        }
                                        //    }
                                        //}




                                        //graphics.Clear(System.Drawing.Color.Gainsboro);
                                        //graphics.DrawRectangle(System.Drawing.Pens.Red, pRect);

                                        /* 
                                         List<KeyValuePair<int, double>> pointL = new List<KeyValuePair<int, double>>();                                       

                                         for (int j = 0; j < 3; j++)
                                         {
                                             double dist;
                                             /////////////////////////////////////////////////////////////////////////////////
                                             dist = Math.Sqrt(Math.Pow(avg.X - phone.Markers[j].x, 2) 
                                                 + Math.Pow(avg.Y - phone.Markers[j].y, 2)
                                                 + Math.Pow(avg.Z - phone.Markers[j].z, 2));
                                             /////////////////////////////////////////////////////////////////////////////////
                                             KeyValuePair<int, double> point = new KeyValuePair<int, double>(phone.Markers[j].ID,dist);

                                             pointL.Add(point);
                                         }

                                         pointL = pointL.OrderBy(point => point.Value).ToList();

                                         pTopLeft = new Point3D(phone.Markers[pointL[2].Key - 1].x, phone.Markers[pointL[2].Key - 1].y, phone.Markers[pointL[2].Key - 1].z);
                                         pTopRight = new Point3D(phone.Markers[pointL[0].Key - 1].x, phone.Markers[pointL[0].Key - 1].y, phone.Markers[pointL[0].Key - 1].z);
                                         pBottomRight = new Point3D(phone.Markers[pointL[1].Key - 1].x, phone.Markers[pointL[1].Key - 1].y, phone.Markers[pointL[1].Key - 1].z);

                                         Size3D rSize = new Size3D(Math.Abs(pTopRight.X - pTopLeft.X), Math.Abs(pTopRight.Y - pBottomRight.Y), Math.Abs(pTopLeft.Z - pBottomRight.Z));
                                         // problem


                                         pBottomLeft = new Point3D((pTopLeft.X - pTopRight.X) + (pBottomRight.X - pTopRight.X) + pTopRight.X, 
                                             (pTopLeft.Y - pTopRight.Y) + (pBottomRight.Y - pTopRight.Y) + pTopRight.Y, 
                                             (pTopLeft.Z - pTopRight.Z) + (pBottomRight.Z - pTopRight.Z) + pTopRight.Z);


                                         Vector3 trtl = new Vector3((float)(pTopLeft.X - pTopRight.X), (float)(pTopLeft.Y - pTopRight.Y), (float)(pTopLeft.Z - pTopRight.Z));
                                         Vector3 trbr = new Vector3((float)(pBottomRight.X - pTopRight.X), (float)(pBottomRight.Y - pTopRight.Y), (float)(pBottomRight.Z - pTopRight.Z));

                                         Vector3 norm = Vector3.Normalize(Vector3.Cross(trbr, trtl));
                                         norm.Normalize();



                                         if ((wall.x < 0 && norm.X > 0) || (wall.x > 0 && norm.X < 0))
                                         {                                            
                                             //facing away from wall, do nothing
                                         } else
                                         {                                            
                                             Point3D eye = new Point3D((pTopLeft.X + (pTopLeft.X - rSize.X)) / 2, (pTopLeft.Y + (pTopLeft.Y - rSize.Y)) / 2, (pTopLeft.Z + (pTopLeft.Z - rSize.Z)) / 2);
                                             eye.Offset((-1) * norm.X, (-1) * norm.Y, (-1) * norm.Z);

                                             Vector3 eyeVec = new Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);                                        

                                             // above is correct

                                             Vector3 eTL = new Vector3((float)(pTopLeft.X - eye.X), (float)(pTopLeft.Y - eye.Y), (float)(pTopLeft.Z - eye.Z));
                                             Vector3 eTR = new Vector3((float)(pTopRight.X - eye.X), (float)(pTopRight.Y - eye.Y), (float)(pTopRight.Z - eye.Z));
                                             Vector3 eBL = new Vector3((float)(pBottomLeft.X - eye.X), (float)(pBottomLeft.Y - eye.Y), (float)(pBottomLeft.Z - eye.Z));
                                             Vector3 eBR = new Vector3((float)(pBottomRight.X - eye.X), (float)(pBottomRight.Y - eye.Y), (float)(pBottomRight.Z - eye.Z));

                                             Ray eTLr = new Ray(eyeVec, eTL);
                                             Ray eTRr = new Ray(eyeVec, eTR);
                                             Ray eBLr = new Ray(eyeVec, eBL);
                                             Ray eBRr = new Ray(eyeVec, eBR);

                                             Nullable<float> eTLd = eTLr.Intersects(wallPlane);
                                             Nullable<float> eTRd = eTRr.Intersects(wallPlane);
                                             Nullable<float> eBLd = eBLr.Intersects(wallPlane);
                                             Nullable<float> eBRd = eBRr.Intersects(wallPlane);

                                             //eTLr.Direction.Normalize();
                                             //eTRr.Direction.Normalize();
                                             //eBLr.Direction.Normalize();
                                             //eBRr.Direction.Normalize();

                                             if (eTLd.HasValue && eTRd.HasValue && eBLd.HasValue && eBRd.HasValue)
                                             {
                                                 Point3D projTL = new Point3D(eTLr.Direction.X * (float)eTLd, eTLr.Direction.Y * (float)eTLd, eTLr.Direction.Z * (float)eTLd);
                                                 Point3D projTR = new Point3D(eTRr.Direction.X * (float)eTRd, eTRr.Direction.Y * (float)eTRd, eTRr.Direction.Z * (float)eTRd);
                                                 Point3D projBR = new Point3D(eBLr.Direction.X * (float)eBLd, eBLr.Direction.Y * (float)eBLd, eBLr.Direction.Z * (float)eBLd);
                                                 Point3D projBL = new Point3D(eBRr.Direction.X * (float)eBRd, eBRr.Direction.Y * (float)eBRd, eBRr.Direction.Z * (float)eBRd);

                                                 System.Drawing.Graphics graphics = this.CreateGraphics();

                                                 //PointF p1 = new PointF(phone.Markers[0].x, phone.Markers[0].y, phone.Markers[0].z);

                                                 //float xProj = ((float)Math.Atan(11.3) * z) / zPos;                                       
                                                 //float yProj = ((float)Math.Atan(6) * z) / zPos;

                                                 PointF[] polyPoints = new PointF[4];
                                                 //polyPoints[0].X = (float)(Math.Abs(eye.Z - topLeft.Z) / xSize) * xScale;
                                                 polyPoints[0].X = (float)((Math.Abs(projTL.Z - topLeft.Z) / xSize) - 1) * xScale;
                                                 label1.Text = polyPoints[0].X.ToString();
                                                 //polyPoints[0].Y = (float)(Math.Abs(eye.Y - topLeft.Y) / ySize) * yScale;
                                                 polyPoints[0].Y = (float)((topLeft.Y - projTL.Y) / ySize) * yScale;
                                                 label2.Text = polyPoints[0].Y.ToString();

                                                 polyPoints[1].X = (float)((Math.Abs(projTR.Z - topLeft.Z) / xSize) - 1) * xScale;//projTR.Z;
                                                 label3.Text = polyPoints[1].X.ToString();
                                                 polyPoints[1].Y = (float)((topLeft.Y - projTR.Y) / ySize) * yScale;
                                                 label4.Text = polyPoints[1].Y.ToString();

                                                 polyPoints[2].X = (float)((Math.Abs(projBR.Z - topLeft.Z) / xSize) - 1) * xScale;//projBR.Z;
                                                 label5.Text = polyPoints[2].X.ToString();
                                                 polyPoints[2].Y = (float)((topLeft.Y - projBR.Y) / ySize) * yScale;
                                                 label6.Text = polyPoints[2].Y.ToString();

                                                 polyPoints[3].X = (float)((Math.Abs(projBL.Z - topLeft.Z) / xSize) - 1) * xScale;//projBL.Z;
                                                 label7.Text = polyPoints[3].X.ToString();
                                                 polyPoints[3].Y = (float)((topLeft.Y - projBL.Y) / ySize) * yScale;
                                                 label8.Text = polyPoints[3].Y.ToString();

                                                 //graphics.DrawPolygon(Pens.Red, polyPoints);

                                                 graphics.DrawEllipse(Pens.Gainsboro, new RectangleF(pastPoints[0].X - 2, pastPoints[0].Y - 2, 4, 4));
                                                 graphics.DrawEllipse(Pens.Gainsboro, new RectangleF(pastPoints[1].X - 2, pastPoints[1].Y - 2, 4, 4));
                                                 graphics.DrawEllipse(Pens.Gainsboro, new RectangleF(pastPoints[2].X - 2, pastPoints[2].Y - 2, 4, 4));
                                                 graphics.DrawEllipse(Pens.Gainsboro, new RectangleF(pastPoints[3].X - 2, pastPoints[3].Y - 2, 4, 4));

                                                 graphics.DrawEllipse(Pens.Red, new RectangleF(polyPoints[0].X - 2, polyPoints[0].Y - 2, 4, 4));
                                                 graphics.DrawEllipse(Pens.Red, new RectangleF(polyPoints[1].X - 2, polyPoints[1].Y - 2, 4, 4));
                                                 graphics.DrawEllipse(Pens.Red, new RectangleF(polyPoints[2].X - 2, polyPoints[2].Y - 2, 4, 4));
                                                 graphics.DrawEllipse(Pens.Red, new RectangleF(polyPoints[3].X - 2, polyPoints[3].Y - 2, 4, 4));

                                                 pastPoints[0] = polyPoints[0];
                                                 pastPoints[1] = polyPoints[1];
                                                 pastPoints[2] = polyPoints[2];
                                                 pastPoints[3] = polyPoints[3];

                                                 //graphics.Clear(System.Drawing.Color.Gainsboro);
                                                 //graphics.DrawRectangle(System.Drawing.Pens.Red, rectangle);
                                             }

                                         }
                                         */

                                    }
                                }

                                if (rbDef.Name == "Wall")
                                {
                                    wall = rb;

                                    //place holder just to get something working. 
                                    for (int j = 0; j < wall.nMarkers; j++)
                                    {
                                        if (wall.Markers[j].y > 1.5)
                                        {
                                            if (wall.Markers[j].z > 0)
                                            {
                                                topLeft.X = wall.Markers[j].x;
                                                topLeft.Y = wall.Markers[j].y;                                                
                                                topLeft.Z = wall.Markers[j].z;
                                          
                                            }
                                            else
                                            {
                                                topRight.X = wall.Markers[j].x;
                                                topRight.Y = wall.Markers[j].y;                                                
                                                topRight.Z = wall.Markers[j].z;
                                            }
                                        }
                                        else
                                        {
                                            if (wall.Markers[j].z > 0)
                                            {
                                                bottomLeft.X = wall.Markers[j].x;
                                                bottomLeft.Y = wall.Markers[j].y;                                                
                                                bottomLeft.Z = wall.Markers[j].z;
                                            }
                                            else
                                            {
                                                bottomRight.X = wall.Markers[j].x;
                                                bottomRight.Y = wall.Markers[j].y;                                                
                                                bottomRight.Z = wall.Markers[j].z;
                                            }
                                        }

                                    }

                                    //also hacked together for a quick demo

                                    xSize = Math.Abs(bottomRight.Z - topLeft.Z);
                                    xSize = xSize / 2;
                                    ySize = Math.Abs(bottomRight.Y - topLeft.Y);

                                    Vector3 v1, v2, v3;

                                    v1 = new Vector3();
                                    v1.X = wall.Markers[0].x;
                                    v1.Y = wall.Markers[0].y;
                                    v1.Z = wall.Markers[0].z;

                                    v2 = new Vector3();
                                    v2.X = wall.Markers[1].x;
                                    v2.Y = wall.Markers[1].y;
                                    v2.Z = wall.Markers[1].z;

                                    v3 = new Vector3();
                                    v3.X = wall.Markers[2].x;
                                    v3.Y = wall.Markers[2].y;
                                    v3.Z = wall.Markers[2].z;       

                                    wallPlane = new Plane(v1, v2, v3);                                    

                                }
                            }


                        }

                    }
                }
            }

            uiIntraFrameTimer.Stop();
            double uiIntraFrameDuration = uiIntraFrameTimer.Duration();
            m_UIUpdateTimer.Start();

        }
        #region more stuff
        public int LowWord(int number)
        {
            return number & 0xFFFF;
        }

        public int HighWord(int number)
        {
            return ((number >> 16) & 0xFFFF);
        }

        double RadiansToDegrees(double dRads)
        {
            return dRads * (180.0f / Math.PI);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            mApplicationRunning = false;
            m_NatNet.Uninitialize();
        }
        
        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {

        }

        private void menuClear_Click(object sender, EventArgs e)
        {
        }

        private void menuPause_Click(object sender, EventArgs e)
        {
            mPaused = menuPause.Checked;
        }
        
        // Wrapper class for the windows high performance timer QueryPerfCounter
        // ( adapted from MSDN https://msdn.microsoft.com/en-us/library/ff650674.aspx )
        public class QueryPerfCounter
        {
            [DllImport("KERNEL32")]
            private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

            [DllImport("Kernel32.dll")]
            private static extern bool QueryPerformanceFrequency(out long lpFrequency);

            private long start;
            private long stop;
            private long frequency;
            Decimal multiplier = new Decimal(1.0e9);

            public QueryPerfCounter()
            {
                if (QueryPerformanceFrequency(out frequency) == false)
                {
                    // Frequency not supported
                    throw new Win32Exception();
                }
            }

            public void Start()
            {
                QueryPerformanceCounter(out start);
            }

            public void Stop()
            {
                QueryPerformanceCounter(out stop);
            }

            // return elapsed time between start and stop, in milliseconds.
            public double Duration()
            {
                double val = ((double)(stop - start) * (double)multiplier) / (double)frequency;
                val = val / 1000000.0f;   // convert to ms
                return val;
            }
        }
    #endregion


    //
    //
    //  None of the below has actually been tied into the program yet, adding it here so that it can be integrated in the future
    //  - Need to add the message queue
    //  - Planning on adding the init stuff to the connect() function, so that the initial messages get sent back and forth to set up everything *before* the motive frames start getting received and clogging the queue
    //  - Once everything is set up, then the crop messages can be added to the queue. These can be generated in the... UpdateUI Function? Been a while since I worked on this last, I can't quite remember...
    //

    Queue<MessageContent> messageQueue;
    private readonly SynchronizationContext synchronizationContext;
    string ipAddressString = "10.10.123.191"; // This will need to be updated to the correct ip of the computer being used.


    private void handleSockets()
    {
      Task.Factory.StartNew(() =>
      {
        Send();
      });

      Task.Factory.StartNew(() =>
      {
        Recieve();
      });
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

        Console.Write("The server is running at port 8001...\n");
        Console.Write("The local End point is  :" + myList.LocalEndpoint + "\n");
        Console.Write("Waiting for a connection.....\n");
        s = myList.AcceptSocket();
        Console.Write("Connection accepted from " + s.RemoteEndPoint + "\n");

        i = 0;
        img = Resources.SecretImage;
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

      Console.Write("Connecting... \n");

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

      Console.Write("Connection Established\n");

      while (true)
      {
        byte[] messageSizeBytes = new byte[4];
        byte[] messageTypeBytes = new byte[1];
        char messageType;
        ImageConverter converter = new ImageConverter();

        stm.Read(messageTypeBytes, 0, 1);

        messageType = Encoding.ASCII.GetChars(messageTypeBytes)[0];

        Console.Write(messageType.ToString());

        MessageContent newMessage = new MessageContent();

        if (messageType == 'R')
        {

          newMessage.ContentType = 'I';
          newMessage.MessageBytes = (byte[])converter.ConvertTo(Resources.SecretImage, typeof(byte[]));

          messageQueue.Enqueue(newMessage);

        }
        else if (messageType == 'P')
        {
          //newMessage.ContentType = 'T';

        // Not sure what was supposed to happen here, but I'll leave it for now.

        }


      }
    }


  }

}