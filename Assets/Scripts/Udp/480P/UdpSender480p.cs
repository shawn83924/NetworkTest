using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class UdpSender480p : MonoBehaviour
{
    Camera Cam;
    Texture2D image;
    /// <summary>
    /// 原始影像畫面
    /// </summary>
    byte[][] ViewData = new byte[2][];
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
    private Thread[] ViewDataProcessing = new Thread[2];
    /// <summary>
    /// 中斷圖形處理 Thread 的 Gate
    /// </summary>
    private AutoResetEvent[] DataProcessGate = new AutoResetEvent[2];

    /// <summary>
    /// 傳送的Frame號碼
    /// </summary>
    private int FrameNum;

    // Start is called before the first frame update
    void Start()
    {
        Cam = GetComponent<Camera>();
        image = new Texture2D(Cam.targetTexture.width, Cam.targetTexture.height, TextureFormat.RGB24, false);
        ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5555);
        //ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5555);
        udpClient = new UdpClient();
        
        FrameNum = 0;
        for (int i = 0; i < DataProcessGate.Length; i++) { DataProcessGate[i] = new AutoResetEvent(false); }
        for (int i = 0; i < ViewDataProcessing.Length; i++) { ViewDataProcessing[i] = new Thread(ProcessView); }
        for (int i = 0; i < ViewDataProcessing.Length; i++) { ViewDataProcessing[i].Start(i); }
        
        StartCoroutine(ProcessGraphic());
        
    }

    /// <summary>
    /// 進行圖形壓縮以及傳送
    /// </summary>
    /// <param name="Th_Num">Thread Number</param>
    private void ProcessView(object Th_Num)
    {
        int ThreadNumber = (int)Th_Num;
        while (true)
        {
            //先中斷執行序，等待呼叫執行
            DataProcessGate[ThreadNumber].WaitOne();
            
            //壓縮影像
            byte[] CompressView = Compress(ViewData[ThreadNumber]);

            lock (udpClient)
            {
                //開始切割分批傳送
                for (int i = 0, j = 0; i < CompressView.Length; i += 65400, j++)
                {
                    //建立要傳送的封包
                    byte[] SendBuffer;
                    //設定封包大小，最大為 65412，最後 12byte 是作為驗證使用欄位
                    if (i + 65400 <= CompressView.Length) { SendBuffer = new byte[65412]; }
                    else { SendBuffer = new byte[CompressView.Length - i + 12]; }
                    //複製壓縮封包區段給傳送封包
                    Array.Copy(CompressView, i, SendBuffer, 0, SendBuffer.Length - 12);
                    //在後綴加上 Frame number, 切割 Number, 總壓縮長度
                    Array.Copy(BitConverter.GetBytes(FrameNum), 0, SendBuffer, SendBuffer.Length - 12, 4);
                    Array.Copy(BitConverter.GetBytes(j), 0, SendBuffer, SendBuffer.Length - 8, 4);
                    Array.Copy(BitConverter.GetBytes(CompressView.Length), 0, SendBuffer, SendBuffer.Length - 4, 4);

                    //送出封包
                    try
                    {
                        //傳送
                        udpClient.Send(SendBuffer, SendBuffer.Length, ipEndPoint);
                    }
                    catch (Exception e)
                    {
                        Debug.Log(e);
                    }

                }
                FrameNum++;
            }
            
        }
    }

    IEnumerator ProcessGraphic() 
    {
        //處理圖形的Thread編號
        int ProcessNumber = 0;

        while (true)
        {
            //掃描有無等待的Thread
            int i;
            for (i = 0; i < ViewDataProcessing.Length; i++) 
            {
                if (ViewDataProcessing[i].ThreadState == ThreadState.WaitSleepJoin) 
                {
                    ProcessNumber = i;//有等待的 Thread 就紀錄 Thread 編號
                    break;
                }
            }
            //無等待的Thread就等待至下一個Frame
            if (i >= ViewDataProcessing.Length) { yield return null; continue; }

            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = Cam.targetTexture;

            Cam.Render();

            image.ReadPixels(new Rect(0, 0, Cam.targetTexture.width, Cam.targetTexture.height), 0, 0);
            image.Apply();

            RenderTexture.active = currentRT;
            ViewData[ProcessNumber] = image.GetRawTextureData();

            DataProcessGate[ProcessNumber].Set();
            yield return null;
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
        GZipStream zip = new GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, true);

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
            ViewDataProcessing[i].Interrupt();
            ViewDataProcessing[i].Abort();
        }
    }
}

