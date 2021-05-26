using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

public class UdpServer1080p : MonoBehaviour
{
    /// <summary>
    /// 接收資料的 ip 跟 port
    /// </summary>
    private IPEndPoint ipEndPoint;
    /// <summary>
    /// 接收資料的udp Client
    /// </summary>
    private UdpClient udpClient;
    /// <summary>
    /// 進行圖像接收的Thread
    /// </summary>
    private Thread receiveThread;

    /// <summary>
    /// 接收到的圖像 bytes
    /// </summary>
    private byte[] receiveByte;
    /// <summary>
    /// 解壓縮後的圖像 Bytes，最後 2 Bytes 中，1 byte 會作為切割編號，1 byte會作為fram編號
    /// </summary>
    byte[][] ViewFragment = new byte[10][];
    /// <summary>
    /// 畫面的圖像 Buffer
    /// </summary>
    private byte[][] ViewData = new byte[5][];
    /// <summary>
    /// 每個 Buffer 的封包組合次數
    /// </summary>
    private byte[] ViewDataCollectCount = new byte[5];

    /// <summary>
    /// 要渲染給RenderTexture的圖形
    /// </summary>
    Texture2D image;
    /// <summary>
    /// 要被渲染的Rendertexture
    /// </summary>
    public RenderTexture TargetTexture;
    /// <summary>
    /// 負責進行圖形處理的Thread
    /// </summary>
    private Thread[] ViewDataProcessing = new Thread[10];
    /// <summary>
    /// 中斷圖形處理 Thread 的 Gate
    /// </summary>
    private static AutoResetEvent[] DataProcessGate = new AutoResetEvent[10];
    /// <summary>
    /// 更新影像的條件變數，如果為 false 就不能更新
    /// </summary>
    private bool ReceiveLock = false;
    /// <summary>
    /// 更新的fram編號
    /// </summary>
    private byte FramNum;


    void Start()
    {
        Application.targetFrameRate = 90;
        ipEndPoint = new IPEndPoint(IPAddress.Any, 5555);
        udpClient = new UdpClient(ipEndPoint.Port);
        image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        //初始化切分後影像的空間
        for (int i = 0; i < ViewFragment.Length; i++) { ViewFragment[i] = new byte[1920 * 1080 * 3 / 8 + 2]; }
        //初始化畫面圖像Buffer
        for (int i = 0; i < ViewData.Length; i++) { ViewData[i] = new byte[1920 * 1080 * 3]; }

        //初始設定8個影像處理的thread
        for (int i = 0; i < ViewDataProcessing.Length; i++) { ViewDataProcessing[i] = new Thread(ProcessView); }
        //初始設定 Thread Gate
        for (int i = 0; i < DataProcessGate.Length; i++) { DataProcessGate[i] = new AutoResetEvent(false); }
        //開始執行影像處理Thread
        for (int i = 0; i < ViewDataProcessing.Length; i++) { ViewDataProcessing[i].Start(i); }
        receiveThread = new Thread(ReceiveView);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }
    void ProcessView(object obj)
    {
        int ThreadNumber = (int)obj;
        while (true)
        {
            //先中斷執行序，等待呼叫執行
            DataProcessGate[ThreadNumber].WaitOne();
            //解壓縮資料
            lock (receiveByte) { ViewFragment[ThreadNumber] = Decompress(receiveByte); }
            //取得 fram 編號，數字為0~4
            byte FramNumber = (byte)(ViewFragment[ThreadNumber][ViewFragment[ThreadNumber].Length - 1] % 5);

            //取得切割編號
            byte SegmentNumber = ViewFragment[ThreadNumber][ViewFragment[ThreadNumber].Length - 2];
            // 將收到的資料寫入Buffer中， i 為畫面資料編號， j 為解壓縮資料編號
            for (int i = SegmentNumber * 3, j = 0; i < ViewData[FramNumber].Length; i += 24, j += 3)
            {
                ViewData[FramNumber][i] = ViewFragment[ThreadNumber][j];
                ViewData[FramNumber][i + 1] = ViewFragment[ThreadNumber][j + 1];
                ViewData[FramNumber][i + 2] = ViewFragment[ThreadNumber][j + 2];
            }
            // Buffer 封包數量 +1
            lock (ViewDataCollectCount)
            {
                //ViewDataCollectCount[FramNumber] += (byte)(1 << ThreadNumber);
                ViewDataCollectCount[FramNumber] += 1;
                //如果該 Buffer 封包量到達8個，畫出該 Buffer 畫面
                if (ViewDataCollectCount[FramNumber] == 8)
                {
                    //歸 0 Buffer 封包數量
                    ViewDataCollectCount[FramNumber] = 0;
                    //也歸 0 前8個 Buffer 封包數量
                    ViewDataCollectCount[(ViewDataCollectCount.Length + FramNumber - 1) % 5] = 0;
                    ViewDataCollectCount[(ViewDataCollectCount.Length + FramNumber - 2) % 5] = 0;
                    ViewDataCollectCount[(ViewDataCollectCount.Length + FramNumber - 3) % 5] = 0;
                    ViewDataCollectCount[(ViewDataCollectCount.Length + FramNumber - 4) % 5] = 0;
                    //設定要更新的 Fram 編號
                    FramNum = FramNumber;
                    //將Lock設為True
                    ReceiveLock = true;
                }
            }

        }
    }

    void ReceiveView()
    {
        while (true)
        {
            //接收資料
            receiveByte = udpClient.Receive(ref ipEndPoint);
            
            //搜尋正在睡眠的 Thread
            for (int i = 0; i < ViewDataProcessing.Length; i++)
            {
                if (ViewDataProcessing[i].ThreadState == ThreadState.WaitSleepJoin) 
                {
                    //找到後解封睡眠，讓該 Thread 處理封包資料
                    DataProcessGate[i].Set();
                    break;
                }
            }
        }
    }

    private void Update()
    {
        //如果現在還在接收封包就停止更新畫面
        if (ReceiveLock == false) { return; }
        
        //更新Texture2D資料
        image.LoadRawTextureData(ViewData[FramNum]);
        image.Apply();
        //將Texture2D圖形更新至RenderTexture
        Graphics.Blit(image, TargetTexture);
        //停止畫面更新，改為封包接收
        ReceiveLock = false;

    }
    
    private void OnDisable()
    {
        udpClient.Close();
        receiveThread.Join();
        receiveThread.Abort();
    }

    private void OnDestroy()
    {
        receiveThread.Abort();
        for (int i = 0; i < ViewDataProcessing.Length; i++)
        {
            DataProcessGate[i].Set();
            ViewDataProcessing[i].Interrupt();
            ViewDataProcessing[i].Abort();
        }
    }

    /// <summary>
    /// 壓縮演算法
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public byte[] Compress(byte[] data)
    {
        MemoryStream ms = new MemoryStream();
        GZipStream zip = new GZipStream(ms, CompressionMode.Compress, true);
        zip.Write(data, 0, data.Length);
        zip.Close();
        ms.Position = 0;

        byte[] compressed = new byte[ms.Length];
        ms.Read(compressed, 0, compressed.Length);

        byte[] gzBuffer = new byte[compressed.Length + 4];
        Buffer.BlockCopy(compressed, 0, gzBuffer, 4, compressed.Length);
        Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, gzBuffer, 0, 4);
        return gzBuffer;
    }
    
    /// <summary>
    /// 解壓縮演算法
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public byte[] Decompress(byte[] data)
    {
        MemoryStream ms = new MemoryStream();
        int msgLength = BitConverter.ToInt32(data, 0);
        ms.Write(data, 4, data.Length - 4);

        byte[] buffer = new byte[msgLength];

        ms.Position = 0;
        GZipStream zip = new GZipStream(ms, CompressionMode.Decompress);
        zip.Read(buffer, 0, buffer.Length);
        zip.Close();
        return buffer;
    }
}
