using System;
using System.Collections.Generic;         // Container as: List<T>, Dictionary...
using System.Linq;                        // IEnumerable
//using System.Text;                      // Stringbuilder

using Sandbox.ModAPI.Ingame;              // Base class and access point
using Sandbox.ModAPI.Interfaces;          // Block properties
using VRageMath;                          // SE physics math
//using SpaceEngineers.Game.ModAPI.Ingame;  // More blocktypes (MyGravitationGenerator)
//using VRage.Game.ModAPI.Ingame;           // Return types

namespace HoverController
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
// last edit 271217
/**************
 *** FIELDS ***
 **************/

// const build values
const string mcDbgLcdName = "Debug-LCD";           // Name of debug LCD-block
const string mcGyroGrpName = "Gyro-System";        // Group with dedicated gyros
const string mcCtrlName = "Fernsteuerung";         // Main ship controller
const string mcEngineGrpName = "Auftrieb-Gruppe";  // Group with dedicated thrusters
const string mcLaserScannerName = "LaserScanner";  // Kamera used for forward distance scan

// init variables
int mSampleTime = 100;            // Intervall between program procs [ms]
float mMaxSpd = 5;                // Max allowed vertical speed
float mHeightOffset = 1.2f;       // Height of cockpit at ground level
bool mEnDampeners=true;           // Allows to change dampener state if set to true
bool mEnGyro=true;                // Allows Gyro overwride
bool mEnThrust=true;              // Allows Thrust overwride
short[] mScannerFOV= {30,17};     // Half-With Viewfield of laserscanner (x,y) in degree to look for obstacles

List<IMyGyro> mGyros;
IMyShipController mCtrl;
List<IMyThrust> mEngines;
IMyCameraBlock mScanner;
Matrix mRefOrInGrid;
Pid mAlg;
Heartbeat mWatchdog;

// dynamic variables
string mDbgMessage;//use appendToLog to access
double mDesiredHeight;
float mDesiredPitch;
float mDesiredRoll;
float mMaxEnginePwr;  // Max Engine Power [N] (One out of the group, weakest value)
double mElapsed;
double mGravityMagn; // = 9.81;
double mLastHeight;
short mCurrentViewAngle;
bool isClearArea;

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
  // Initialize blocks
  mGyros = new List<IMyGyro>( );
  mEngines = new List<IMyThrust>( );
  mWatchdog = new Heartbeat( (str)=>appendToLog(str) );
  isClearArea = true;

  if( !init( ) ) {
    disableCtrl( );
    appendToLog( "\nInit failed" );
  }

  assignStrData( Storage );
  Echo( mDbgMessage );
}


// Called when the program needs to save its state. Use
// this method to save your state to the Storage field
// or some other means.
// This method is optional and can be removed if not
// needed.
public void Save()
{
  Storage = "desiredHeight = " + mDesiredHeight.ToString( "0.00" ) + "\n";
  Storage += "lastHeight = " + mLastHeight.ToString( ) + "\n";
  // more
}


// The main entry point of the script, invoked every time
// one of the programmable block's Run actions are invoked.
// The method itself is required, but the argument above
// can be removed if not needed.
public void Main(string argument)
{
  mDbgMessage = "HoverController Ingame-Script"; //reset

  // Reading command arguments.
  string[] args = argument.Split( '=' );
  //Empty Argument = length 1
  if( args.Length > 0 ) {
    switch( args[0] ) {

    // (Re-)Read CustomInfo and initialize Program:
    case "init":
      if( !init( ) )
        appendToLog( "HoverController - Init failed" );
      Echo( mDbgMessage );
      return;  // program done

    case "disable":
      disableCtrl( );
      Echo( "HoverController - Disabled" );
      return;  // program done

    case "reset":
      if( mAlg != null ) {
        mAlg.reset( );
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
        Echo( "HoverController - Reset" );
      }
      return;

    case "setHeight":
      if( args.Length > 1 && !string.IsNullOrEmpty( args[1] ) ) {
        mDesiredHeight = double.Parse( args[1].Trim() );
        Echo( "Height set to " + args[1] );
      }
      return;

    case "setPitch":
      if( args.Length > 1 && !string.IsNullOrEmpty( args[1] ) ) {
        mDesiredPitch = float.Parse( args[1].Trim() );
        Echo( "Pitch set to " + args[1] );
      }
      return;

    case "setRoll":
      if( args.Length > 1 && !string.IsNullOrEmpty( args[1] ) ) {
        mDesiredRoll = float.Parse( args[1].Trim() );
        Echo( "Roll set to " + args[1] );
      }
      return;

    case "enableDampeners":
      if( args.Length > 1 && !string.IsNullOrEmpty( args[1] ) ) {
        mEnDampeners = bool.Parse( args[1].Trim() );
        Echo( "Dampeners set: " + args[1] );
      }
      return;

    case "powerLimit":
      if( args.Length > 1 && !string.IsNullOrEmpty( args[1] ) ) {
        mMaxEnginePwr = float.Parse( args[1].Trim() );
        Echo( " Limit set to " + args[1] );
      }
      return;
    }// switch
  } // if argument.count

  /// update10(167ms) is too slow
  mElapsed += Runtime.TimeSinceLastRun.TotalMilliseconds;
  
  if( ((int)mElapsed % 8) == 0 )
    isClearArea&=scanIsClearedArea( 10 );
    
  if( mElapsed < mSampleTime )
    return;
  
  mElapsed = 0.0;
  mWatchdog.tick();

  double val = 0;
  if( mCtrl != null ) {
    val = alignToGravity(
      MatrixD.Multiply(
        MatrixD.CreateFromYawPitchRoll( 0,-mDesiredPitch,-mDesiredRoll),
        mCtrl.WorldMatrix.GetOrientation( ) )
    );
    appendToLog( "\nAngle Difference: ", val );

    if( val > 45.0 || !mCtrl.TryGetPlanetElevation( MyPlanetElevation.Surface, out val ) ){
      appendToLog( "\nHeight: Invalid" );
      applyThrust( .0f, mMaxEnginePwr );  // disable thrust override
      if( mEnDampeners )
        mCtrl.DampenersOverride = true;  // give control backt to dampeners
    } else {
      val -= mHeightOffset;
      appendToLog( "\nHeight: ", val );

      if( mEnThrust ){
        // Craft has no downward thrust, it uses gravity to come down.
        // However, if we set thrusteroverride=0, the dampeners stop all movement.
        // Disable dampeners to fall down

        if( mEnDampeners )
          mCtrl.DampenersOverride = (  mDesiredHeight-val >= 0.5 );

        //if( !senseIsClearedArea( "Sensor" ) ){
        if( !isClearArea ){
          isClearArea=true;
          val -= 3;
          appendToLog( "\nObstacle detected" );
        }

        val = applyThrust( procThrustWithHeight( val ), mMaxEnginePwr );

      } //enThrust
    } // else
  } //mCtrl defined

  appendToLog( "\nThrust: ", val );

  outputToLCD( mcDbgLcdName, mDbgMessage );
  Echo( mDbgMessage );

}// main


/*********************
  *** HELPER METHODS ***
  ********************/

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
  Dictionary<string, string> assigns = parseKeyAssigns( data );

  foreach( KeyValuePair<string, string> kvp in assigns )
    appendToLog( "\n" + kvp.Key + " <--> " + kvp.Value );

  if( assigns.ContainsKey( "lastHeight" ) ) {
    mLastHeight = double.Parse( assigns["lastHeight"] );
    checksum++;
  }

  if( assigns.ContainsKey( "desiredHeight" ) ) {
    mDesiredHeight = double.Parse( assigns["desiredHeight"] );
    checksum++;
  }

  return checksum;
}


public class Heartbeat
{
  byte mHeartbeat=0;
  Action<string> print;

  public Heartbeat(Action<string> printAction){this.print=printAction;}

  public void tick()
  {
    if(null == print)
      return;

    mHeartbeat++;
    switch( mHeartbeat ) {
    case 1:
      print( "  |" );
      break;

    case 2:
      print( " /" );
      break;

    case 3:
      print( " --" );
      break;

    case 4:
      print( " \\" );
      mHeartbeat = 0;
      break;
    }//switch
  }//tick

}//class heartbeat


/********************
 ***CUSTOM METHODS***
 ********************/

bool init()
{
  appendToLog( "\nHoverController::init()" );

  Pid.Params par;
  compileParams( out par );
  if( mAlg == null )
    mAlg = new Pid( par );
  else {
    mAlg.reset( );
    mAlg.setup( par );
  }

  mGyros.Clear( );
  IMyBlockGroup grp = GridTerminalSystem.GetBlockGroupWithName( mcGyroGrpName );
  if( grp != null ) {
    grp.GetBlocksOfType( mGyros );
    appendToLog( "\nGyros found: ", mGyros.Count );
  }

  mEngines.Clear( );
  grp = GridTerminalSystem.GetBlockGroupWithName( mcEngineGrpName );
  if( grp != null ) {
    grp.GetBlocksOfType( mEngines );
    appendToLog( "\nEngines found: ", mEngines.Count );
  }

  mCtrl = GridTerminalSystem.GetBlockWithName( mcCtrlName ) as IMyShipController;
  mScanner = GridTerminalSystem.GetBlockWithName( mcLaserScannerName ) as IMyCameraBlock;

  // Checkup
  bool ok = ( mAlg != null ); //&& mScanner != null );

  if( ok && ( mGyros != null ) )
    ok = mGyros.Count > 0;

  if( ok && ( mCtrl != null ) ) {
    mCtrl.Orientation.GetMatrix( out mRefOrInGrid );
    mCtrl.TryGetPlanetElevation( MyPlanetElevation.Surface, out mLastHeight );
    ok = mRefOrInGrid.IsValid( );
  }

  if( ok && ( mEngines != null ) ) {
    if( mEngines.Count > 0 )
      mMaxEnginePwr = evalEnginePwr( );

    ok = mMaxEnginePwr > 0;
  }

  if( mScanner != null )
    mScanner.EnableRaycast = true;

  mCurrentViewAngle = mScannerFOV[0];

  Runtime.UpdateFrequency = ok? UpdateFrequency.Update1 : UpdateFrequency.None;

  return ok;
}


double updateGravity(out Vector3D g)
{
  if( mCtrl != null ) {
    g = mCtrl.GetNaturalGravity( );
    mGravityMagn = g.Length( );
    g.Normalize( );
    return mGravityMagn;
  } else {
    g = new Vector3D( 0, 0, 0 );
    return 0.0;
  }
}


double alignToGravity(MatrixD refOr)
{
  // refOr describes transformation to block orientation in world system

  // Axis:
  // up green vert y yaw lh-rot
  // right red horz x pitch rh-rot
  // forward blue lat z roll rh-rot
  // max rot 6.28 == 60rpm == 2Pi for small gyro
  //         3.12 == 30rpm == pi for big gyro

  if( mGyros.Count == 0 )
    return 0.0;

  Vector3D refDown = refOr.Down;

  // world coordinate vector at reference point
  Vector3D grav;
  if( updateGravity( out grav ) < 0.1 ) {
    appendToLog( "\nHoverController::alignToGravity returned - Low planetary gravity" );
    return 0.0;
  }

  // dot returns cosinus between two vectors. acos of -1..1 is 0 to pi
  double angle = Math.Acos( MathHelper.Clamp( Vector3D.Dot( refDown, grav ), -1, 1 ) );
  if( angle < 0.01  || !mEnGyro) { //<0.57°
    for( int i = 0; i < mGyros.Count; i++ )
      mGyros[i].GyroOverride = false;

    return angle;
  }

  // Cross returns orientation of rotation axis (world coordinates)
  // Mind the order
  Vector3D rot = Vector3D.Cross( grav, refDown );
  rot.Normalize( );  // magnitude is of no interest

  // rotation axis relative to reference block
  // We need inverse transformation block -> origin
  // Transpose of orthogonal matrices is the inverse mat
  // Rotation matrices are always orthogonal (using cartesian coordinate system)
  rot = Vector3D.Transform( rot, MatrixD.Transpose( refOr ) );
  rot = Vector3D.Transform( rot, mRefOrInGrid ); //only if gyros not aligned

  applyRotation( rot, angle );

  // angle difference in degree
  return angle / MathHelper.Pi * 180.0;
}


void applyRotation( Vector3D rot, double angle )
{
  int nGyro = 0;
  bool isLargeGrid;
  Vector3D localRot;
  Matrix gyOr;
  do {
    mGyros[nGyro].GyroOverride = true;
    isLargeGrid = (int)mGyros[nGyro].CubeGrid.GridSizeEnum == 1;  // enum blacklisted

    // align rotation to n-th gyro, if not same orientation as reference
    mGyros[nGyro].Orientation.GetMatrix( out gyOr );
    localRot = Vector3D.Transform( rot, MatrixD.Transpose( gyOr ) );

    // max applicable rotation magnitude
    localRot *= isLargeGrid ?
    angle : angle * 2.0; // same as: div MathHelper.Pi * 6.28;

    // Yaw is not reversed rotation in code
    mGyros[nGyro].Yaw = Convert.ToSingle( localRot.Y );
    mGyros[nGyro].Pitch = Convert.ToSingle( localRot.X );
    mGyros[nGyro].Roll = Convert.ToSingle( localRot.Z );

    nGyro++;
  } while( nGyro < mGyros.Count );

}


void disableCtrl()
{
  appendToLog( "\nHoverController::disableCtrl()" );

  for( int i = 0; i < mGyros.Count; i++ )
    mGyros[i].GyroOverride = false;

  for( int i = 0; i < mEngines.Count; i++ )
    mEngines[i].SetValueFloat( "Override", .0f );

  if( mEnDampeners )
        mCtrl.DampenersOverride = true;

  Runtime.UpdateFrequency = UpdateFrequency.None;
}


int compileParams(out Pid.Params _par)
{
  _par.ampP = 1.0;          // reactivity factor, depends on gravity and mass
  _par.enableD = false;  // differentation unecessary for inert objects
  _par.enableI = true;     // integration for ship weight and gravity
  _par.lowerCap = .0f;   //  max fall speed
  _par.upperCap = mMaxSpd;  // max rise speed
  _par.tD = 0.001;
  _par.tI = 0.8;               // Integration time for adapting to ship mass
  _par.threshold = 0.01f; // Sensitivity for differences
  _par.tSample = 0.1;      // Regulation interval

  Dictionary<string, string> lut = parseKeyAssigns( Me.CustomData );

  if( lut.ContainsKey( "ampP" ) )
    _par.ampP = double.Parse( lut["ampP"] );

  if( lut.ContainsKey( "enableI" ) )
    _par.enableI = bool.Parse( lut["enableI"] );

  if( lut.ContainsKey( "tI" ) )
    _par.tI = double.Parse( lut["tI"] );

  if( lut.ContainsKey( "SampleTime" ) ) {
    mSampleTime = int.Parse( lut["sampleTime"] );  //[msec]
    _par.tSample = mSampleTime / 1000.0; // [sec]
  }

  if( lut.ContainsKey( "upperCap" ) )
    _par.upperCap = mMaxSpd = float.Parse( lut["upperCap"] );

  if( lut.ContainsKey( "lowerCap" ) )
    _par.lowerCap = float.Parse( lut["lowerCap"] );

  if( lut.ContainsKey( "HeightOffset" ) )
    mHeightOffset = float.Parse( lut["HeightOffset"] );

  if( lut.ContainsKey( "EnDampeners" ) )
    mEnDampeners = bool.Parse( lut["enableDampeners"] );

  if( lut.ContainsKey( "EnGyro" ) )
    mEnGyro = bool.Parse( lut["EnGyro"] );

  if( lut.ContainsKey( "EnThrust" ) )
    mEnThrust = bool.Parse( lut["EnThrust"] );

  return lut.Count;
}


float procThrustWithHeight(double h)
{
  if( mAlg == null )
    return 0;

  // current speed
  mAlg.feedbackVal = 1000.0 * ( h - mLastHeight ) / mSampleTime;  // ts [ms]
  mLastHeight = h;

  // desired speed
  h = mDesiredHeight - h;  // h[m] / 1[s] -> meters per second
  if( Math.Abs( h ) > mMaxSpd ) {
    mAlg.desiredVal = h < 0 ? -mMaxSpd : mMaxSpd;
  } else
    mAlg.desiredVal = h;

  mAlg.proc( );

  // Level-Shift
  // Use full power on max spd. request
  return Convert.ToSingle( Math.Max( mAlg.result / mMaxSpd, .0 ) );
}


float evalEnginePwr()
{
  float maxPwr = float.MaxValue;
  float thrust;
  bool isValid = false;
  for( int i = 0; i < mEngines.Count; i++ ) {
    thrust = mEngines[i].MaxThrust;
    // get smallest common value
    if( thrust < maxPwr ) {
      maxPwr = thrust;
      isValid = true;
    }
  }

  return isValid == true ? maxPwr : .0f;
}


float applyThrust(float magnitude, float powerLim)
{
  if( magnitude < .0f )
    magnitude = .0f;

  // Apply thrust
  float pwr;
  float N = .0f;
  foreach( IMyThrust th in mEngines ) {
    pwr = th.MaxEffectiveThrust;
    if( pwr <= 0 )
      pwr = 1.0f;
    pwr = magnitude * powerLim / pwr;
    // clip range
    if( pwr > 1.0f )
      pwr = 1.0f;

    th.SetValueFloat( "Override", pwr * 100.0f );
    N += pwr * th.MaxEffectiveThrust;
  }

  return N;
}


private bool scanIsClearedArea(double range)
{
  if (mScanner == null )
    return true;

  MyDetectedEntityInfo mInfo = new MyDetectedEntityInfo();
  
  //ready to scan designated distance?
  if (mScanner.CanScan(range)){
    //[todo] comensate vertical angle depend on ship tilt
    mInfo = mScanner.Raycast(range,-mScannerFOV[1],mCurrentViewAngle);
    if(mCurrentViewAngle < mScannerFOV[0])
      mCurrentViewAngle+=5;
    else
      mCurrentViewAngle=(short)(-mScannerFOV[0]);
    
  }

  if ( mInfo.IsEmpty() )
    return true;
  else
    return !mInfo.HitPosition.HasValue;  //not empty and no value is never the case
}

private bool senseIsClearedArea(string sensorName, int withinRange=50)
{
  IMySensorBlock sens = GridTerminalSystem.GetBlockWithName(sensorName) as IMySensorBlock;
  int count=0;
  if( null != sens ){
    List<MyDetectedEntityInfo> infoList = new List<MyDetectedEntityInfo>();
    sens.DetectedEntities( infoList );
    Vector3D pos = sens.GetPosition( );
    if( infoList.Count > 0)
      count = infoList.Count( info => !info.IsEmpty( ) && Math.Abs(Vector3D.Distance( pos, info.Position )) < withinRange );

  }
  return count == 0;
}



/********************
 *** Custom Class ***
 ********************/

public class Pid
{
  /*==== Members====*/

  public struct Params
  {
    public double ampP;
    public double tI;
    public double tD;
    public double tSample;
    public bool enableI;
    public bool enableD;
    public float upperCap;
    public float lowerCap;
    public float threshold;
  }

  /*==== Fields ====*/

  double mW;  // Desired value.
  double mX;  // Current value ( feedback )
  double mY;  // Result
  readonly float[] mLimits = new float[3];
  readonly double[] mXdk = new double[3];
  readonly double[] mCoeff_a = new double[3];
  readonly double[] mCoeff_b = { 1.0, .0 };
  bool mIsLimited = true;

  /*==== Properties ====*/

  public double result {
    get { return mY; }
  }


  public double desiredVal {
    set { mW = value; }
  }


  public double feedbackVal {
    set { mX = value; }
  }

  /*==== Methods ====*/

  public Pid(Params par)
  {
    setup( par );
  }


  public void reset()
  {
    mW = .0;
    mY = .0;  // y1_k-0
    mXdk[0] = .0;  // xd_k-0
    mXdk[1] = .0;  // xd_k-1
    mXdk[2] = .0;  // xd_k-2
  }


  public void restoreState(double dx, double y)
  {
    mY = y;
    mXdk[1] = dx;  // xd_k-1
  }


  public void readState(out double dx, out double y)
  {
    dx = mXdk[0];
    y = mY;
  }


  public void setup(Params par)
  {
    // Zero-division guard
    if( par.tI <= 0 ) par.tI = .001;
    if( par.tSample <= 0 ) par.tSample = .001;

    /* PID algorythm (forward-euler)
    mCoeff_a[0] = par.ampP * ( 1. + par.tSample / par.tI + par.tD / par.tSample );
    mCoeff_a[1] = -par.ampP * ( 1. + 2. * par.tD / par.tSample );
    mCoeff_a[2] = par.ampP * par.tD / par.tSample;*/

    mCoeff_a[0] = 1.0;
    mCoeff_b[1] = .0;

    // PID algorythm trapezoidal
    if( par.enableI ) {
      double kI = par.tSample / ( 2.0 * par.tI );
      mCoeff_b[1] = 1.0;
      mCoeff_a[0] += kI;
      mCoeff_a[1] = -1.0 + kI;
    }

    if( par.enableD ) {
      double kD = par.tD / par.tSample;
      mCoeff_a[0] += kD;
      if( par.enableI ) {
        mCoeff_a[1] -= 2.0 * kD;
        mCoeff_a[2] += kD;
      } else
        mCoeff_a[1] -= kD;
    }

    mCoeff_a[0] *= par.ampP;
    mCoeff_a[1] *= par.ampP;
    mCoeff_a[2] *= par.ampP;

    // Limit caps
    mLimits[0] = par.lowerCap;
    mLimits[1] = par.upperCap;
    mLimits[2] = par.threshold;

    // Given that Limits are all default .0f
    mIsLimited = ( Math.Abs( mLimits[0] - mLimits[1] ) > mLimits[2] );
  }


  public void proc()
  {
    // b0=1 not considered
    // previous output values y_k-0 -> y_k-1
    //qDebug() << "elapsed: " << mExecTime.restart();

    mXdk[0] = mW - mX;
    // irrelevant difference?
    if( Math.Abs( mXdk[0] ) < mLimits[2] )
      mXdk[0] = 0;

    mY = mCoeff_b[1] * mY  // y_k-1
      + mCoeff_a[0] * mXdk[0]   // xd_k-0
      + mCoeff_a[1] * mXdk[1]   // xd_k-1
      + mCoeff_a[2] * mXdk[2];  // xd_k-2

    // limits enabled?
    if(mIsLimited) {
      // maximum
      if( mY > mLimits[1] )
        mY = mLimits[1];
      // minimum
      else if( mY < mLimits[0] )
        mY = mLimits[0];
    }

    // push of values, old xd_k-2 is lost
    mXdk[2] = mXdk[1];
    mXdk[1] = mXdk[0];
  }
}

#endregion

}// class
}// namespace
