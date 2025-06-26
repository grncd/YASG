using UnityEngine;
using ExitGames.Client.Photon;
using Photon.Realtime;
using Photon.Voice;
using System;
using System.Threading;

namespace DataStreamDemo
{
    public class DataStreamClient : MonoBehaviour
    {
        public string AppId;
        public string AppVersion = "1";
        public string Region = "EU";
        public const string RoomName = "PhotonDataStream";
        public Photon.Voice.LogLevel LogLevel = Photon.Voice.LogLevel.Info;
        [Space(10)]
        public int FPS = 30;
        public int FrameSize = 2000;
        public int DecodeDelayFrames = 3;
        public bool Echo;

        Codec DataStreamCodec = Codec.Custom1;
        bool started;
        ByteStreamEncoder encoder;
        LocalVoice localDataStream;

        // Separate media in channels for better Photon transport performance
        enum Channel
        {
            DataStream = 1
        }

        public VoiceClient VoiceClient => lbt.VoiceClient;
        LoadBalancingTransport lbt;

        protected Photon.Voice.Unity.Logger logger = new Photon.Voice.Unity.Logger();

        protected virtual void Start()
        {
            logger.Level = LogLevel;
            lbt = new LoadBalancingTransport2(logger);

            lbt.LoadBalancingPeer.DebugOut = DebugLevel.INFO;
            lbt.LoadBalancingPeer.TrafficStatsEnabled = true;
            lbt.AppId = AppId;
            lbt.AppVersion = AppVersion;
            lbt.StateChanged += (ClientState stateOld, ClientState s) =>
            {
                logger.Log(LogLevel.Info, $"LBC: state: {s}");
                switch (s)
                {
                    case ClientState.ConnectedToMasterServer:
                        lbt.OpJoinRandomOrCreateRoom(null, new EnterRoomParams()
                        {
                            RoomName = RoomName,
                            RoomOptions = new RoomOptions() { MaxPlayers = 5 } // the UI limits the number of incoming video streams to 4
                        });
                        break;
                    case ClientState.Joined:
                        // recreate voices to update from settings possibly changed by user in lobby
                        CreateDataStream();
                        break;
                    case ClientState.Disconnected:
                        RemoveDataStream();
                        break;
                }
            };

            VoiceClient.SetRemoteVoiceDelayFrames(DataStreamCodec, DecodeDelayFrames);


            VoiceClient.OnRemoteVoiceInfoAction += OnRemoteVoiceAdd;

            Connect();

            Debug.LogFormat("LBC: init");
            started = true;
        }


        protected virtual void Update()
        {
            if (!started)
            {
                return;
            }

            // apply settings dynamically
            VoiceClient.SetRemoteVoiceDelayFrames(DataStreamCodec, DecodeDelayFrames);
            if (localDataStream != null)
            {
                this.localDataStream.DebugEchoMode = Echo;
            }

            lbt.Service();
        }

        protected void OnApplicationQuit()
        {
            this.Disconnect();
        }


        public void Connect()
        {
            lbt.ConnectToRegionMaster(Region);
        }

        public void Disconnect()
        {
            if (lbt != null)
            {
                lbt.Disconnect();
            }
        }

        // Called by VoiceClient for every new stream
        private void OnRemoteVoiceAdd(int channelId, int playerId, byte voiceId, VoiceInfo i, ref RemoteVoiceOptions options)
        {
            if (i.Codec == DataStreamCodec)
            {
                options.Decoder = new ByteStreamDecoder(consumeDecoderOutput, () => Debug.LogWarning("Decoder missing frame"));
            }
            else
            {
                Debug.LogErrorFormat("LBC: " + "unsupported codec " + i.Codec);
            }
        }

        bool streaming = false;
        protected void CreateDataStream()
        {
            var voiceInfo = new VoiceInfo()
            {
                Codec = DataStreamCodec
            };

            encoder = new ByteStreamEncoder();

            var options = new VoiceCreateOptions()
            {
                Encoder = encoder,
                EventBufSize = 4 * 256, // receiving buffer of increased size
                Fragment = true,
                Reliable = true,
            };

            localDataStream = VoiceClient.CreateLocalVoice(voiceInfo, (int)Channel.DataStream, options);

            new Thread(produceEncoderInput).Start();
        }

        protected void RemoveDataStream()
        {
            streaming = false;
            if (this.localDataStream != null)
            {
                this.localDataStream?.RemoveSelf();
            }
        }

        int cnt = 0;
        int nextReport = Environment.TickCount + 1000;

        void produceEncoderInput()
        {
            Debug.LogFormat("Streaming start");
            streaming = true;
            byte[] buf = null;
            System.Random rand = new System.Random();
            while (streaming)
            {
                if (buf == null || buf.Length != FrameSize + 4)
                {
                    buf = new byte[FrameSize + 4];
                }
                rand.NextBytes(buf);
                var hash = BitConverter.GetBytes(Util.CalculateCrc(buf, 0, buf.Length - 4));
                Array.Copy(hash, 0, buf, buf.Length - 4, 4);

                encoder.Input(buf);
                Thread.Sleep(1000 / FPS);
            }
            Debug.LogFormat("Streaming stop");
        }

        // Normally called from Decoder Input() in a worker thread.
        // Use buf.Retain() / Release() if you need buf to be valid after return.
        void consumeDecoderOutput(ref FrameBuffer buf)
        {
            var hash = Util.CalculateCrc(buf.Array, buf.Offset, buf.Length - 4);
            if (hash != BitConverter.ToUInt32(buf.Array, buf.Offset + buf.Length - 4))
            {
                Debug.LogError("Decoder corrupted frame");
            }

            cnt += buf.Length;
            var t = Environment.TickCount;
            if (t - nextReport > 0)
            {
                Debug.LogFormat("Decoder received {0} bytes/sec", cnt);
                cnt = 0;
                nextReport = t + 1000;
            }
        }
    }
}