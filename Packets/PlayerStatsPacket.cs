using LiteNetLib.Utils;

namespace PlayerStatsSync {
    public struct PlayerStatsPacket : INetSerializable {
        public int FPS {get;set;}
        public string Nickname {get;set;}
        public string ProfileID {get;set;}

        public PlayerStatsPacket (int FPS, string Nickname, string ProfileID) {
            this.FPS = FPS;
            this.Nickname = Nickname;
            this.ProfileID = ProfileID;
        }

        public void Deserialize(NetDataReader reader){
            this.FPS = reader.GetInt();
            this.Nickname = reader.GetString();
            this.ProfileID = reader.GetString();
        }

        public void Serialize(NetDataWriter writer){
            writer.Put(FPS);
            writer.Put(Nickname);
            writer.Put(ProfileID);
        }
    }
}