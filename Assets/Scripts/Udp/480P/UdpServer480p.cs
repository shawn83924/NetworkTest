using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class UdpServer480p : MonoBehaviour
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
    /// 接收到的圖像 bytes
    /// </summary>
    private byte[] receiveByte;
    /// <summary>
    /// 接收圖像的Queue
    /// </summary>
    Queue<byte[]> RecvQueue;
    /// <summary>
    /// 畫面的圖像 Buffer
    /// </summary>
    private byte[] ViewData = new byte[854 * 480 * 3];
    /// <summary>
    /// 接收的Frame編號
    /// </summary>
    int FrameNum;

    /// <summary>
    /// 要渲染給RenderTexture的圖形
    /// </summary>
    Texture2D image;
    /// <summary>
    /// 要被渲染的Rendertexture
    /// </summary>
    public RenderTexture TargetTexture;
    /// <summary>
    /// 負責進行接收封包的Thread
    /// </summary>
    private Thread receiveThread;
    /// <summary>
    /// 負責進行圖形處理的Thread
    /// </summary>
    private Thread ProcessThread;
    /// <summary>
    /// 渲染開關，為True則無法渲染畫面
    /// </summary>
    private bool RenderLock = true;

    // Start is called before the first frame update
    void Start()
    {
        ipEndPoint = new IPEndPoint(IPAddress.Any, 5555);
        udpClient = new UdpClient(ipEndPoint.Port);
        image = new Texture2D(TargetTexture.width, TargetTexture.height, TextureFormat.RGB24, false);
        FrameNum = 0;
        RecvQueue = new Queue<byte[]>();

        receiveThread = new Thread(new ThreadStart(RecvViewData));
        receiveThread.Start();
        ProcessThread = new Thread(new ThreadStart(ProcessViewData));
        ProcessThread.Start();
        StartCoroutine(ProcessGraphic());
    }

    IEnumerator ProcessGraphic() 
    {
        while (true) 
        {
            if (RenderLock == true) { yield return null; continue; }

            //更新Texture2D資料
            image.LoadRawTextureData(ViewData);
            //yield return new WaitUntil(() => { image.LoadImage(ViewData); return false; });
            image.Apply();
            //將Texture2D圖形更新至RenderTexture
            Graphics.Blit(image, TargetTexture);
            RenderLock = true;
        }
    }
    private void RecvViewData()
    {
        while (true)
        {
            //接收資料
            receiveByte = udpClient.Receive(ref ipEndPoint);
            //將資料放入佇列儲存
            RecvQueue.Enqueue(receiveByte);

        }
    }
    private void ProcessViewData()
    {
        //一整個Frame的壓縮Array
        byte[] FrameData = new byte[0];
        //從 Queue 提取的封包資料
        byte[] recvData;
        //一個 Frame 的壓縮片段數量
        int FragmentCount = 0;

        while (true)
        {

            //確認RecvQueue是否有資料，有就抓，沒有就繼續偵測
            if (RecvQueue.Count > 0) { recvData = RecvQueue.Dequeue(); }
            else { continue; }

            //收到的data的Frame number
            int GetFrameNumber = BitConverter.ToInt32(recvData, recvData.Length - 12);

            //如果收到的data的Frame number小於目前處理的Frame Number，直接跳過
            if (GetFrameNumber < FrameNum) { continue; }
            //如果收到的data的Frame number大於目前處理的Frame Number，代表收到新的Frame，就 Reset 處理的資料
            else if (GetFrameNumber > FrameNum) 
            {
                FrameData = new byte[0];
                FrameNum = GetFrameNumber;
                //Debug.Log(FrameNum);
            }

            //將data加入byte array中
            //如果是第一個封包片段要組合
            if (FrameData.Length == 0)
            {
                //宣告符合壓縮封包長度的Array
                FrameData = new byte[BitConverter.ToInt32(recvData, recvData.Length - 4)];
                //宣告封包片段總量
                FragmentCount = decimal.ToInt32(Math.Ceiling(Convert.ToDecimal((float)FrameData.Length / 65400)));
            }

            //將收到的data加入壓縮的Array
            Array.Copy(recvData, 0, FrameData, BitConverter.ToInt32(recvData, recvData.Length - 8) * 65400, recvData.Length - 12);
            FragmentCount--;

            //Frame資料收集滿
            if (FragmentCount == 0)
            {
                //解壓縮資料
                ViewData = Decompress(FrameData);

                FrameData = new byte[0];
                RenderLock = false;
            }
        }
    }

    private void OnDestroy()
    {
        receiveThread.Interrupt();
        receiveThread.Abort();
        ProcessThread.Interrupt();
        ProcessThread.Abort();

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
