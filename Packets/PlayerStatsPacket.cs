using LiteNetLib.Utils;

namespace PlayerStatsSync {
    public struct PlayerStatsPacket : INetSerializable {
        public int FPS {get;set;}
        public string Nickname {get;set;}
        public string ProfileID {get;set;}
        public string Timestamp{get;set;}
        public bool IsDedi{get;set;}
        public bool IsServer{get;set;}

        public PlayerStatsPacket (int FPS, string Nickname, string ProfileID, string Timestamp, bool IsDedi, bool IsServer){
            this.FPS = FPS;
            this.Nickname = Nickname;
            this.ProfileID = ProfileID;
            this.Timestamp = Timestamp;
            this.IsDedi = IsDedi;
            this.IsServer = IsServer;
        }

        public void Deserialize(NetDataReader reader){
            this.FPS = reader.GetInt();
            this.Nickname = reader.GetString();
            this.ProfileID = reader.GetString();
            this.Timestamp=reader.GetString();
            this.IsDedi=reader.GetBool();
            this.IsServer=reader.GetBool();
        }

        public void Serialize(NetDataWriter writer){
            writer.Put(FPS);
            writer.Put(Nickname);
            writer.Put(ProfileID);
            writer.Put(Timestamp);
            writer.Put(IsDedi);
            writer.Put(IsServer);
        }
    }
}