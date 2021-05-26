using UnityEngine;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System;

/// <summary>
/// 此程式為接收Client 連線訊號，並透過不斷傳送字串偵測對方是否還存在
/// </summary>
public class CheckingServer : MonoBehaviour
{
    Socket serverSocket; //伺服器端socket
    Socket[] clientSockets = new Socket[10]; //客戶端socket
    IPEndPoint ipEnd; //偵聽埠
    string recvStr; //接收的字串
    string sendStr; //傳送的字串
    byte[] recvData = new byte[1024]; //接收的資料，必須為位元組
    byte[] sendData = new byte[1024]; //傳送的資料，必須為位元組
    int recvLen; //接收的資料長度

    Thread connectThread; //連線執行緒
    Thread[] RecvThread = new Thread[10];//接收執行緒
    public int RecvNum;//連線端的數量
    string editString;

    
    void Start()
    {
        //在這裡初始化server
        InitSocket(); 
        //不斷傳送data，確認Client是否還活著
        StartCoroutine(CheckClientSurvive());
    }

    void InitSocket()
    {
        //定義偵聽埠,偵聽任何本機 IP 的 5566 Port
        ipEnd = new IPEndPoint(IPAddress.Any, 5566);
        //定義套接字型別,在主執行緒中定義
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        //連線
        serverSocket.Bind(ipEnd);
        //開始偵聽,最大10個連線
        serverSocket.Listen(10);

        //開啟接收連線執行緒
        connectThread = new Thread(new ThreadStart(SocketConnet));
        connectThread.Start();
        
        RecvNum = 0;
        //開啟多接收執行緒
        for (int i = 0; i < RecvThread.Length; i++) { RecvThread[i] = new Thread(SocketReceive); }

    }

    //連線
    void SocketConnet()
    {
        while (true)
        {
            if (RecvNum == RecvThread.Length) { continue; }

            //控制檯輸出偵聽狀態
            print("Waiting for a client");
            //一旦接受連線，建立一個客戶端
            Socket socket = serverSocket.Accept();
            //將得到的 Socket 加入Array中
            int SocketIndex = AddClientSocket(socket);
            if (SocketIndex == clientSockets.Length) { continue; }

            //獲取客戶端的IP和埠
            IPEndPoint ipEndClient = (IPEndPoint)clientSockets[SocketIndex].RemoteEndPoint;
            //輸出客戶端的IP和埠
            print("Connect with " + ipEndClient.Address.ToString() + ":" + ipEndClient.Port.ToString());
            //連線成功則傳送資料
            sendStr = "Welcome to my server";
            SocketSend(sendStr, SocketIndex);

        }
    }


    //伺服器接收
    void SocketReceive(object RecvNumber)
    {
        //Thread編號
        int num = (int)RecvNumber;
        //進入接收迴圈
        while (true)
        {
            //對data清空
            recvData = new byte[1024];
            //獲取收到的資料的長度
            try
            {
                recvLen = clientSockets[num].Receive(recvData);
            }
            catch (Exception e) 
            { 
                Debug.Log(e);
                //如果對方已斷線，接收不到資料，則斷開Socket連線，清除相關參數

                if (clientSockets[num].RemoteEndPoint == null) 
                {
                    RemoveClientSocket(num);
                }

            }
            //如果收到的資料長度為0，則進入下一個迴圈
            if (recvLen == 0)
            {
                continue;
            }
            //輸出接收到的資料
            recvStr = Encoding.ASCII.GetString(recvData, 0, recvLen);
            print(recvStr);
            //將接收到的資料經過處理再發送出去
            sendStr = "From Server: " + recvStr;
            SocketSend(sendStr, num);
        }
    }

    
    void SocketSend(string sendStr, int num)
    {
        //清空傳送快取
        sendData = new byte[1024];
        //資料型別轉換
        sendData = Encoding.ASCII.GetBytes(sendStr);
        //傳送
        try
        {
            clientSockets[num].Send(sendData, sendData.Length, SocketFlags.None);
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
    }
    /// <summary>
    /// 傳送Data給Client的協程，每0.1秒傳一次，確認Client端是否還活著
    /// </summary>
    /// <returns></returns>
    IEnumerator CheckClientSurvive() 
    {
        while (true) 
        {
            for (int i = 0; i < clientSockets.Length; i++)
            {
                if (clientSockets[i] != null)
                    SocketSend("ServerCheck", i);
            }
            yield return new WaitForSeconds(0.1f);
        }
    }
    void OnGUI()
    {
        editString = GUI.TextField(new Rect(10, 10, 100, 20), editString);
        if (GUI.Button(new Rect(10, 30, 60, 20), "send")) 
        {
            for (int i = 0; i < clientSockets.Length; i++) 
            {
                if (clientSockets[i] != null)
                    SocketSend(editString, i);
            }
        }

    }
    //連線關閉
    void SocketQuit()
    {
        //關閉執行緒
        if (connectThread != null)
        {
            connectThread.Interrupt();
            connectThread.Abort();
        }
        //關閉客戶端
        for (int i = 0; i < clientSockets.Length; i++)
        {
            if (clientSockets[i] != null)
                clientSockets[i].Close();
        }
        for (int i = 0; i < RecvThread.Length; i++) 
        {
            RecvThread[i].Interrupt();
            RecvThread[i].Abort();
        }
        //最後關閉伺服器
        serverSocket.Close();
        print("diconnect");
    }


    void OnApplicationQuit()
    {
        SocketQuit();
    }

    /// <summary>
    /// 新增 ClientSocket 至 Array 中
    /// </summary>
    /// <param name="ClientSocket"></param>
    int AddClientSocket(Socket ClientSocket) 
    {
        int i = 0;
        //尋找Array中為null的位置
        for (; i < clientSockets.Length;i++) 
        {
            if (clientSockets[i] == null) { break; }
        }
        if (i >= clientSockets.Length) { return clientSockets.Length; }

        lock (clientSockets)
        {
            clientSockets[i] = ClientSocket;
            RecvNum++;
        }
        //啟動監聽該 Socket 的 Thread
        RecvThread[i].Start(i);
        //回傳Array中的Index
        return i;
    }

    /// <summary>
    /// 切斷該Socket連線
    /// </summary>
    /// <param name="ClientIndex"></param>
    void RemoveClientSocket(int ClientIndex) 
    {
        Debug.Log("RemoveClientSocket");
        if (clientSockets[ClientIndex] != null)
        {
            clientSockets[ClientIndex].Close();
            clientSockets[ClientIndex] = null;
        }
        RecvNum--;
        Thread CloseThread = RecvThread[ClientIndex];
        RecvThread[ClientIndex] = new Thread(SocketReceive);
        CloseThread.Interrupt();
        CloseThread.Abort();
    }

}
