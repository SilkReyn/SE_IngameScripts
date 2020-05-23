using System;
using System.Collections.Generic;         // Container as: List<T>, Dictionary...
//using System.Linq;                        // IEnumerable

using Sandbox.ModAPI.Ingame;              // Base class and access point
using Sandbox.ModAPI.Interfaces;          // Block properties
using VRageMath;                          // SE physics math
//using SpaceEngineers.Game.ModAPI.Ingame;  // More blocktypes
//using VRage.Game.ModAPI.Ingame;           // Return types

namespace FollowerDrone
{
/* Public Base Properties:
 * IMyGridTerminalSystem GridTerminalSystem
 * IMyProgrammableBlock Me
 * obsolete TimeSpan ElapsedTime
 * IMyGridProgramRuntimeInfo Runtime
 * string Storage
 * Action<string> Echo
 * optional Save
 * mandatory Main( string )
 */

public class Program : MyGridProgram
{
#region PB-CODE

/**************
 *** FIELDS ***
 **************/

// const build values  
const string mcDbgLcdName = "Debug-LCD";  // Name of debug LCD-block  
const string mcControllerName = "Fernsteuerung";
const string mcThrusterGrp = "Steuer-Gruppe";
readonly string[] mThrusterNames = {"Schub O", "Schub W", "Schub N", "Schub S", "Schub hoch", "Schub runter"};

// init variables  
List<IMyThrust> mThrusters = new List<IMyThrust>();
//IMyShipController mSC;

// references
Follower mFollowerLogic = new Follower();

// dynamic variables  
string mDbgMessage = "";
float mThrustMagnitude = 20.0f;

/****************  
 ***PB PROGRAM***  
 ****************/

// The constructor, called only once every session and  
// always before any other method is called. Use it to  
// initialize your script. String Storage can be used.  
// The constructor is optional and can be removed if not  
// needed.  
public Program()
{
  Dictionary<string, string> lut = parseKeyAssigns( Storage );
  if( lut.Count > 0 && lut.ContainsKey( "Flags" ) )
    mFollowerLogic.load( byte.Parse(lut["Flags"]) );
    
  init();
}


// Called when the program needs to save its state. Use  
// this method to save your state to the Storage field  
// or some other means.   
// This method is optional and can be removed if not  
// needed.  
public void Save()
{
  Storage = mFollowerLogic.save();
}


// The main entry point of the script, invoked every time  
// one of the programmable block's Run actions are invoked.  
// The method itself is required, but the argument above  
// can be removed if not needed.  
public void Main(string argument)
{
  mDbgMessage = "FollowerDrone Ingame-Script"; //reset  

  // Reading command arguments.  
  string[] args = argument.Split( '=' );
  //Empty Argument = length 1  
  if( args.Length > 0 ) {
    switch( args[0] ) {
    
    case "setSens":
    {
      byte val;
      if( args.Length > 1 && byte.TryParse(args[1],out val) ){
        mFollowerLogic.setFlag( (Follower.FlagName)val );
        Echo( "entered Sensor" + val.ToString());
      } else
        return;
     
      break;
    }
    
    case "clrSens":
    {
      Echo("left Sensor");
      byte val;
      if( args.Length > 1 && byte.TryParse(args[1],out val) ) {
        mFollowerLogic.setFlag( (Follower.FlagName)val, false );
        Echo( "entered Sensor" + val.ToString());
      } else
        return;
    
      break;
    }  
    
    case "halt":
      Echo("halt");
      updateThrust(new bool[mThrusters.Count]);
      return;
    
    case "resume":
      Echo("resume");
      break;
      
    default:
      Echo("invalid Argument");
      return;
      
    }  // switch
    
  } // args.Length

  bool[] states;
  mFollowerLogic.getStates(out states);
  updateThrust(states);
  
}// main


void init()
{
  //mSC = GridTerminalSystem.GetBlockWithName(mcControllerName) as IMyShipController;
  mThrusters.Clear();
  
  List<IMyThrust> blocks = new List<IMyThrust>();
  GridTerminalSystem.GetBlockGroupWithName(mcThrusterGrp).GetBlocksOfType(blocks);
  
  IMyThrust t;
  foreach( string name in mThrusterNames){
    t = blocks.Find(x => x.CustomName.Trim() == name);
    if( t != null)
      mThrusters.Add(t);
  }
}


void updateThrust( bool[] overrideOnOff )
{
  if( mThrusters.Count < 1 )
    return;
  
  int cnt = Math.Min( overrideOnOff.Length, mThrusters.Count );
  for( int i=0; i<cnt; i++){
    mThrusters[i].SetValueFloat("Override", overrideOnOff[i] ? mThrustMagnitude : .0f );
  }
  
}

/**************************
  *** EXTENTION METHODS *** 
  *************************/

// Search text for line seperated keys and their assigned '=' name. 
// @param assigns: text that is searched for keys. 
Dictionary<string, string> parseKeyAssigns(string assigns)
{
	string[] lines = assigns.Split( '\n' );
	Dictionary<string, string> dic = new Dictionary<string, string>( );
	string[] lh_rh;

	for( int i = 0; i < lines.Length; i++ ) {
		lh_rh = lines[i].Split( '=' );
		if( lh_rh.Length == 2 && !string.IsNullOrEmpty(lh_rh[1]))
			dic.Add( lh_rh[0].Trim(), lh_rh[1].Trim( ) );
	}// end for keysCount 

	return dic;
}

// Writes text to a named LCD-Panel 
// @param name: Name of the LCD-Panel. 
// @param text: Text to write out to LCD. 
void outputToLCD(string name, string text)
{
	IMyTextPanel lcd = GridTerminalSystem.GetBlockWithName( name ) as IMyTextPanel;
	if( lcd != null )
		lcd.WritePublicText( text );
}


bool isOk(IMyTerminalBlock block)
{
	if( block != null )
		return block.IsWorking;
	else
		return false;
}


void appendToLog(string msg, ValueType num = null, bool appendLine = false)
{
	mDbgMessage += msg;
	if( num != null ) {
		if( num is float | num is double | num is decimal ) {
			mDbgMessage += Convert.ToDouble( num ).ToString( "0.00" );
		} else
			mDbgMessage += num.ToString( );
	}

	if( appendLine )
		mDbgMessage += "\n";
}


// Check string data for variable states
byte assignStrData(string data)
{
	byte checksum = 0;
	Dictionary<string, string> lut = parseKeyAssigns( data );

	foreach( KeyValuePair<string, string> kvp in lut )
		appendToLog( "\n" + kvp.Key + " <--> " + kvp.Value );

	// use data here
  
  
	return checksum;
}



/********************
 *** CUSTOM CLASS *** 
 ********************/

public class Follower
{
  //==== Member ====
  public enum FlagName : byte { PX=0, NX, PY, NY, PZ, NZ, FLAG_CNT };
  
  //==== Fields ====
  byte mFlags;
  readonly bool[] mStates = new bool [(int)FlagName.FLAG_CNT];
  
  //==== Properties ====
  
  
  //==== Calls ====
  
  public Follower()
  {
    
  }
  
  public Follower(byte flags)
  {
    load( flags );
  }
  
  public string save()
  {
    return "Flags=" + mFlags.ToString() + "\n";
  }
  
  public void load(byte flags)
  {
    mFlags = flags;
    updateStates();
  }
  
  //==== Methods ====
  
  public byte setFlag( FlagName k, bool onOff=true )
  {// unfortunately bitwise operator seem to be int return type
    if ( k < FlagName.FLAG_CNT ){
      int mask = 1 << (int)k;
      if ( onOff )  // set
        mFlags = (byte)(mFlags | mask);
      else  // clear
        mFlags = (byte)(mFlags & ~mask );
    }
    
    updateStates();
    
    return mFlags;
  }
  
  bool getFlag( FlagName k )
  {
    return (( 1 << (int)k ) & mFlags) > 0;
  }
  
  void updateStates()
  {
    // copy into array
    for(FlagName k=FlagName.PX; k<FlagName.FLAG_CNT; k++ )
      mStates[(int)k] = getFlag(k);
    
    // release blocking inversed states
    for(byte i=0; i<(byte)FlagName.FLAG_CNT; i+=2){
      if(mStates[i] && mStates[i+1])
        mStates[i] = mStates[i+1] = false;
    }
  }
  
  public bool getState( FlagName flag )
  {
    return mStates[(int)flag];
  }
  
  public void getStates(out bool[] states)
  {
    states = new bool[(int)FlagName.FLAG_CNT];
    mStates.CopyTo(states,0);
  }
  
  
}  // class Follower


#endregion

}// class
}// namespace
