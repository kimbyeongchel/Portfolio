// UDP 연결. Unity 메인 스레드에서 매 프레임 Poll()만 부른다 (별도 스레드·락 없음).
// RPC 문자열 이름도, PhotonView도 없다: 보내는 건 입력, 받는 건 스냅샷뿐.
// 받기·보내기 버퍼와 Snapshot 객체는 하나씩 만들어 두고 재사용한다 (패킷마다 GC 할당 없음).
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LostRuins.Shared;

namespace LostRuins.Client
{
    public sealed class NetClient : IDisposable
    {
        const int SIO_UDP_CONNRESET = -1744830452;   // Windows 전용 소켓 옵션

        readonly Socket socket;
        readonly IPEndPoint server;
        readonly byte[] recvBuffer = new byte[1500];
        readonly BitWriter writer = new BitWriter(Protocol.MaxPacketBytes);
        readonly BitReader reader = new BitReader(new byte[0], 0);
        readonly Snapshot snapshot = new Snapshot();
        readonly AckTracker acks = new AckTracker();
        readonly EventDedup dedup = new EventDedup();
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        ushort sendSeq;
        float connectTimer;
        readonly ClassId wantedClass;

        public bool Connected { get; private set; }
        public ushort PlayerId { get; private set; }
        public MoveState SpawnState { get; private set; }
        public ClassId Class { get; private set; }
        public ushort LastSnapshotTick { get; private set; }   // 지금 처리 중인 스냅샷의 서버 틱 (이벤트에 붙임)
        public long BytesIn { get; private set; }
        public long BytesOut { get; private set; }

        public event Action<Snapshot> SnapshotReceived;    // 넘겨받은 Snapshot은 다음 패킷에서 재사용되므로 보관하지 말 것
        public event Action<GameEvent> EventReceived;      // 중복 제거 후 1번씩

        public NetClient(string host, int port, ClassId cls)
        {
            wantedClass = cls;
            server = new IPEndPoint(IPAddress.Parse(host), port);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // 서버가 꺼져 ICMP "포트 없음"이 오면 Windows는 다음 Receive를 예외로 만든다. 이 동작을 끈다.
                socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
        }

        public void Poll(float deltaSeconds)
        {
            if (!Connected)
            {
                connectTimer -= deltaSeconds;
                if (connectTimer <= 0f) { connectTimer = 0.2f; Send(PacketType.Connect, null); }
            }
            while (socket.Available > 0)
            {
                int n;
                try { n = socket.ReceiveFrom(recvBuffer, ref from); }
                catch (SocketException) { break; }
                if (!from.Equals(server)) continue;           // 서버가 아닌 곳에서 온 패킷은 무시
                BytesIn += n;
                Handle(n);
            }
        }

        public void SendInputs(List<InputCmd> recent)
        {
            if (Connected) Send(PacketType.Input, recent);
        }

        void Send(PacketType type, List<InputCmd> inputs)
        {
            writer.Reset();
            Protocol.WriteHeader(writer, new Header { Type = type, Seq = sendSeq++, Ack = acks.Ack, AckBits = acks.Bits });
            if (type == PacketType.Connect) Protocol.WriteConnect(writer, wantedClass);
            if (inputs != null) Protocol.WriteInputs(writer, inputs);
            socket.SendTo(writer.Buffer, writer.Length, SocketFlags.None, server);
            BytesOut += writer.Length;
        }

        void Handle(int length)
        {
            reader.Reset(recvBuffer, length);
            Header h = Protocol.ReadHeader(reader);
            if (!reader.Ok) return;
            acks.OnReceived(h.Seq);
            if (h.Type == PacketType.Accept)
            {
                var a = Protocol.ReadAccept(reader);
                if (!reader.Ok) return;
                PlayerId = a.PlayerId; Class = a.Cls; SpawnState = a.Spawn;
                Connected = true;
                return;
            }
            if (h.Type != PacketType.Snapshot) return;
            Protocol.ReadSnapshot(reader, snapshot);
            if (!reader.Ok) return;                             // 망가진 패킷은 통째로 버림
            LastSnapshotTick = snapshot.ServerTick;
            var handler = EventReceived;
            for (int i = 0; i < snapshot.Events.Count; ++i)
            {
                var ev = snapshot.Events[i];
                if (dedup.FirstTime(ev.Id) && handler != null) handler(ev);
            }
            var snap = SnapshotReceived;
            if (snap != null) snap(snapshot);
        }

        public void Dispose() { socket.Close(); }
    }
}
