using System;

using Sandbox.ModAPI.Ingame;              // Base class and access point
using Sandbox.ModAPI.Interfaces;          // Block properties
using SpaceEngineers.Game.ModAPI.Ingame;  // More blocktypes
using VRage.Game.ModAPI.Ingame;           // Return types


namespace SE_IngameScript
{
  public class ProgramUnit : Program
  {
    string mInfo = "";

    static void Main()
    {
      new Program().Main("");
    }

    public ProgramUnit()
    {
      Echo = Print;
    }

    public string DetailInfo
    {
      get { return mInfo; }
      private set { mInfo = value; }
    }

    public void Print(string txt)
    {
      mInfo = txt;
      Console.Write(txt);
    }
  }
}
