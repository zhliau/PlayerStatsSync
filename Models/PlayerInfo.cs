
namespace PlayerStatsSync {
    public struct PlayerInfo {
        public string ProfileID { get; set; }
        public int FPS { get; set; }
        public string Nickname { get; set; }
        public bool IsServer { get; set; }
        public bool IsDedi{ get; set; }
    }
}