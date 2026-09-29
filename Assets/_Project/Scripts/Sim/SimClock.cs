namespace ActionGame.Sim
{
    public static class SimClock
    {
        public static int Tick {get; private set;}
        public static void Advance()
        {
            Tick ++;
        }
        public static void Reset()
        {
            Tick = 0;
        }
    }

}
