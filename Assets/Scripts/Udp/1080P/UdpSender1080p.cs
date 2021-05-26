using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System;

public class UdpSender1080p : MonoBehaviour
{
    Camera Cam;
    Texture2D image;
    /// <summary>
    /// 原始影像畫面
    /// </summary>
    byte[] ViewData = new byte[1920 * 1080 * 3];
    /// <summary>
    /// 切分的影像，長度為原始影像的 1/8 + 2 bytes，1 byte 會作為切割編號，1 byte會作為fram編號
    /// </summary>
    byte[][] ViewFragment = new byte[8][];
    /// <summary>
    /// 傳送的對象IP跟port
    /// </summary>
    private IPEndPoint ipEndPoint;
    /// <summary>
    /// UDP傳送者，也就是自己
    /// </summary>
    private UdpClient udpClient;
    /// <summary>
    /// 負責進行圖形處理的Thread
    /// </summary>
    private Thread[] ViewDataProcessing = new Thread[8];
    /// <summary>
    /// 中斷圖形處理 Thread 的 Gate
    /// </summary>
    private AutoResetEvent[] DataProcessGate = new AutoResetEvent[8];
    /// <summary>
    /// 計算圖形的 Thread Counter
    /// </summary>
    private int ProcessingCount = 8;
    /// <summary>
    /// 傳送的Frame編號
    /// </summary>
    private byte[] FramNum = new byte[1];

    private void Start()
    {
        Application.targetFrameRate = 60;
        Cam = GetComponent<Camera>();
        image = new Texture2D(Cam.targetTexture.width, Cam.targetTexture.height, TextureFormat.RGB24, false);
        ipEndPoint = new IPEndPoint(IPAddress.Parse("192.168.0.173"), 5555);
        udpClient = new UdpClient();
        //初始化切分後影像的空間
        for (int i = 0; i < ViewFragment.Length; i++) { ViewFragment[i] = new byte[1920 * 1080 * 3 / 8 + 2]; }
        //初始設定8個影像處理的thread
        for (int i = 0; i < ViewDataProcessing.Length; i++) { ViewDataProcessing[i] = new Thread(ProcessView); }
        //初始設定 Thread Gate
        for (int i = 0; i < DataProcessGate.Length; i++) { DataProcessGate[i] = new AutoResetEvent(false); }
        //開始執行影像處理Thread
        for (int i = 0; i < ViewDataProcessing.Length; i++) { ViewDataProcessing[i].Start(i); }

        #region 影像存檔測試
        /*
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture.active = Cam.targetTexture;
        Cam.Render();
        image.ReadPixels(new Rect(0, 0, Cam.targetTexture.width, Cam.targetTexture.height), 0, 0);
        image.Apply();
        RenderTexture.active = currentRT;

        byte[] bytes = image.EncodeToPNG();
        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", bytes);
        */
        #endregion

    }

    private void ProcessView(object obj)
    {
        int ProcessNumber = (int)obj;
        while (true)
        {
            //先中斷執行序，等待呼叫執行
            DataProcessGate[ProcessNumber].WaitOne();
            //切分圖像
            SplitView(ProcessNumber);
            //壓縮圖像
            byte[] CompressView = Compress(ViewFragment[ProcessNumber]);
            //傳送圖像
            lock (udpClient)
            {
                udpClient.Send(CompressView, CompressView.Length, ipEndPoint);
                ProcessingCount++;
            }
        }
    }

    private void Update()
    {
        
        //如果 ProcessingCount 大於等於 8 ，就更新影像
        if (ProcessingCount >= 8) 
        {
            
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = Cam.targetTexture;

            Cam.Render();

            image.ReadPixels(new Rect(0, 0, Cam.targetTexture.width, Cam.targetTexture.height), 0, 0);
            image.Apply();
            RenderTexture.active = currentRT;
            ViewData = image.GetRawTextureData();

            //更新完影像後先將 ProcessingCount 歸 0 ，防止影像更新
            ProcessingCount = 0;
            //fram 數加 1
            lock (FramNum)
            {
                FramNum[0] += 1;
                if (FramNum[0] == 200) { FramNum[0] = 0; }//限制 FramNum 數字為 0~199
            }
            //開始處理影像
            for (int i = 0; i < DataProcessGate.Length; i++)
            {
                //處理圖形並傳送
                DataProcessGate[i].Set();
            }
        }

    }

    /// <summary>
    /// 切分圖像函示，將原始圖像依照 Byte 切分 8 等分
    /// </summary>
    /// <param name="number">切分編號，數字為 0~7 </param>
    public void SplitView(int number)
    {
        //設定切割編號
        ViewFragment[number][ViewData.Length / 8] = (byte)number;
        //設定fram編號
        ViewFragment[number][ViewData.Length / 8 + 1] = FramNum[0];
        // i 為原始資料編號， j 為寫入資料編號
        for (int i = number * 3, j = 0; i < ViewData.Length; i += 24, j += 3)
        {
            ViewFragment[number][j] = ViewData[i];//R
            ViewFragment[number][j + 1] = ViewData[i + 1];//G
            ViewFragment[number][j + 2] = ViewData[i + 2];//B
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

    void OnApplicationQuit()
    {
        udpClient.Close();
        CloseThread();
    }

    /// <summary>
    /// 關閉Thread
    /// </summary>
    void CloseThread()
    {
        //關閉執行緒
        for (int i = 0; i < ViewDataProcessing.Length; i++) 
        {
            DataProcessGate[i].Set();
            ViewDataProcessing[i].Interrupt();
            ViewDataProcessing[i].Abort();
        }
    }
}
