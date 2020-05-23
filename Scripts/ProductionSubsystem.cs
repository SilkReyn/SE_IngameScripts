#region wrapper
// c#
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
//using System.Timers;

// SE Ingame API
using VRage.Game.ModAPI.Ingame;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

// SE Mod API
//using Sandbox.Game.Entities;
//using VRage.Game.ModAPI;
//using SpaceEngineers.Game.ModAPI;
//using VRage.Utils;
//using Sandbox.ModAPI;

/* Mod API Whitelist
• Sandbox.Common.ObjectBuilders
• Sandbox.Common.ObjectBuilders.Definitions
• Sandbox.Definitions
• Sandbox.Game
• Sandbox.Game.Entities
• Sandbox.Game.EntityComponents
• Sandbox.Game.Lights
• Sandbox.ModAPI
• Sandbox.ModAPI.Weapons
• SpaceEngineers.Game.ModAPI

• VRage
• VRage.Collections
• VRage.Game
• VRage.Game.Components
• VRage.Game.Entity
• VRage.Game.ModAPI
• VRage.Game.ModAPI.Interfaces
• VRage.Game.ObjectBuilders
• VRage.ObjectBuilders
• VRage.Library.Utils
• VRage.ModAPI
• VRage.Utils
• VRage.Voxels
*/

namespace SE_IngameScript
{
  /*
  //     Prints out text onto the currently running programmable block's detail info
  //     area.
  public Action<string> Echo { get; protected set; }
  
  //     Gets the amount of in-game time elapsed from the previous run.
  [Obsolete("Use Runtime.TimeSinceLastRun instead")]
  public virtual TimeSpan ElapsedTime { get; protected set; }
  
  //     Provides access to the grid terminal system as viewed from this programmable
  //     block.
  public virtual IMyGridTerminalSystem GridTerminalSystem { get; protected set; }
  
  //     Gets a reference to the currently running programmable block.
  public virtual IMyProgrammableBlock Me { get; protected set; }
  
  //     Gets runtime information for the running grid program.
  public virtual IMyGridProgramRuntimeInfo Runtime { get; protected set; }
  //
  
  //     Allows you to store data between game sessions.
  public virtual string Storage { get; protected set; }
   */

  // IMyTerminalBlock.CustomData can be used for synced data


# endregion
class Program : MyGridProgram
{

//***FIELDS***
// Name of Group with BlastFurnices
private string userBlastFurniceGrp;
// Name of Group with Cargos holding ingots
private string userSourceCargoGrp;
// Name of Master or first Assembler
private string userMasterAssembler;
// Flag if fields are setup;
private bool bInitialized = false;
// Ag=0,SiO,Mg,U,Pt,Si,Au,Co,Fe,Ni
public static string[] myIngots = {
"Silber", "Stein", "Magnesium",
"Uran", "Platin", "Silizium",
"Gold", "Cobalt", "Eisen", "Nickel"
};
public static string[] ingotDefinitions = {
"Ingot/Silver",
"Ingot/Stone",
"Ingot/Magnesium",
"Ingot/Uranium",
"Ingot/Platinum",
"Ingot/Silicon",
"Ingot/Gold",
"Ingot/Cobalt",
"Ingot/Iron",
"Ingot/Nickel"
};
// Amount of each ingot item
private float[] ingotAmountTresholds = {
7f, 7f, 1f,
1f, 1f, 5f,
4f, 74f, 200f, 24f
}; // not yet saved in config
// Name of Assembler LCD
private string userAssemblerInfo;

//***USER METHODS***

// Search string for keys and their assigned (=) name
private Dictionary<string,string> parseKeyAssigns(string assigns)
{
  string[] lines = assigns.Split('\n');
  int keysCount = lines.Length;
  Dictionary<string,string> dic = new Dictionary<string,string>();
  string[] lh_rh;

  for (int i = 0; i < keysCount; i++)
  {
    lh_rh = lines[i].Split('=');
    if (lh_rh.Length == 2)
      dic.Add(lh_rh[0].TrimEnd(), lh_rh[1].TrimStart());
    
  }// end for keysCount
  return dic;
}// end parseKeyAssigns


private void moveAllItemsFromTo(string A, string B, int idxA=0, int idxB=0)
{
  IMyInventory inventoryA, inventoryB;

  if (A != null && B !=null) //can still fail if A or B block not present
  {
    inventoryA = GridTerminalSystem.GetBlockWithName(A).GetInventory(idxA);
    inventoryB = GridTerminalSystem.GetBlockWithName(B).GetInventory(idxB);

    //int numItems = inventoryA.GetItems().Count;
    //List<IMyInventoryItem> itemList = inventoryA.GetItems();
    //Echo(numItems + " Items found in " + A + ", transferring to " + B);

    // Now loop through each of the items, start at last to maintain index order 
    for (int i = inventoryA.GetItems().Count - 1; i >= 0; i--)
    {
      // here: means invA has at least 1 item
      /*
      IMyInventoryItem item = itemList[i];
      Echo("Menge: " + item.Amount.ToString());
      Echo("Subtype: " + item.GetDefinitionId().SubtypeName); // subtype Iron, Gold
      Echo("Content: " + item.Content.ToString()); // definition ..._Ingot, ..._Ore
      Echo("Id: " + item.ItemId.ToString()); // definition id 3=ingot, 5=ore ?
      /*
      * item.GetType().FullName; generic physical item
      * item.GetDefinitionId().ToString(); definition + subtype ..._Ingot/Gold , ..._Ore/Iron
      * item.Content.SubtypeName; same as subtype
      */
      
      if (!inventoryB.IsFull)
        inventoryA.TransferItemTo(inventoryB, i, null, true); // destination, index, dst_idx, enable stacking, amount=all 
      else
      { // destination is now full, cancel loop 
        Echo(B + " is full");
        break;
      }
    }// for loop
  }// args.Length
}


// Read ini saved at PB custom data and assign user block names
private bool readCustomData()
{
  short checksum = 0;
  Dictionary<string, string> assigns = parseKeyAssigns(Me.CustomData);

  foreach (KeyValuePair<string, string> kvp in assigns)
    Echo(kvp.Key + "<-->" + kvp.Value);
  
  if (assigns.ContainsKey("userBlastFurniceGrp"))
  {
    userBlastFurniceGrp = assigns["userBlastFurniceGrp"];
    checksum++;
  }
  if (assigns.ContainsKey("userSourceCargoGrp"))
  {
    userSourceCargoGrp = assigns["userSourceCargoGrp"];
    checksum++;
  }
  if (assigns.ContainsKey("userMasterAssembler"))
  {
    userMasterAssembler = assigns["userMasterAssembler"];
    checksum++;
  }
  if (assigns.ContainsKey("userAssemblerInfo"))
  {
    userAssemblerInfo = assigns["userAssemblerInfo"];
    checksum++;
  }
  if(assigns.ContainsKey("ingotAmountTreshold"))
  {
    string[] values = assigns["ingotAmountTreshold"].Split(';');
    if (values.Length <= myIngots.Length)
    {
      for (int i = 0; i < values.Length; i++)
        ingotAmountTresholds[i] = float.Parse(values[i]);
          
    }
    //checksum++ (not mandatory)
  }// ingotAmountTreshold

  if (checksum == 4)
    bInitialized = true;
  else
    bInitialized = false;
  
  return bInitialized;
}


private float[] sumIngotAmountsIn(IMyInventory carg)
{
  List<IMyInventoryItem> items = carg.GetItems();
  float[] amounts = new float[myIngots.Length];

  string defId;
  for (int i = 0; i < items.Count; i++)
  {
    defId = items[i].GetDefinitionId().ToString().Split('_')[1];
    //defId = defId.Substring(defId.LastIndexOf('_') + 1);
    // Ag=0,SiO,Mg,U,Pt,Si,Au,Co,Fe,Ni
    switch (defId)
    {
      // cases not linked to ingotDefinitions[]!
      case "Ingot/Silver":
        amounts[0] += (float)items[i].Amount;
        break;
      case "Ingot/Stone":
        amounts[1] += (float)items[i].Amount;
        break;
      case "Ingot/Magnesium":
        amounts[2] += (float)items[i].Amount;
        break;
      case "Ingot/Uranium":
        amounts[3] += (float)items[i].Amount;
        break;
      case "Ingot/Platinum":
        amounts[4] += (float)items[i].Amount;
        break;
      case "Ingot/Silicon":
        amounts[5] += (float)items[i].Amount;
        break;
      case "Ingot/Gold":
        amounts[6] += (float)items[i].Amount;
        break;
      case "Ingot/Cobalt":
        amounts[7] += (float)items[i].Amount;
        break;
      case "Ingot/Iron":
        amounts[8] += (float)items[i].Amount;
        break;
      case "Ingot/Nickel":
        amounts[9] += (float)items[i].Amount;
        break;
    }// switch defId
    
  }// for item.count
  return amounts;
}


private float[] sumIngotAmountsIn(List<IMyInventory> carg)
{
  float[] totalAmounts = new float[myIngots.Length];
  float[] amounts;
  for (int i = 0; i < carg.Count; i++)
  {
    amounts = sumIngotAmountsIn(carg[i]);

    for (int k = 0; k < myIngots.Length; k++)
      totalAmounts[k] += amounts[k];

  }
  return totalAmounts;
}


private float itemAmountIn(IMyInventory cargo, string itemDefinition, List<int> idx)
{
  List<IMyInventoryItem> items = cargo.GetItems();
  float amount=0;
  
  int i = 0;
  if (idx.Count > 0)
    i = idx[0];

  while(i < items.Count)
  {
    if (items[i].GetDefinitionId().ToString().Split('_')[1] == itemDefinition)
    {
      amount += (float)items[i].Amount;
      idx.Add(i);
    }
    i++;
  }// while items.count
  
  return amount;
}


private void outputToLCD(string name, string text)
{
  (GridTerminalSystem.GetBlockWithName(name) as IMyTextPanel).WritePublicText(text);
}


//***PROGRAM***

public Program()
{
  // The constructor, called only once every session and
  // always before any other method is called. Use it to
  // initialize your script. String Storage can be used.
  //
  // The constructor is optional and can be removed if not
  // needed.

  string[] variables = Storage.Split(';');
  if (variables.Length > 13) // remember modifying this value!
  {
    // mind the order
    userBlastFurniceGrp = variables[0];
    userSourceCargoGrp = variables[1];
    userMasterAssembler = variables[2];
    userAssemblerInfo = variables[3];
    for (int i = 0; i < myIngots.Length; i++) // idx 4 to 13
      ingotAmountTresholds[i] = float.Parse(variables[i + 4]);
    
    bInitialized = true;
  }
}


public void Save()
{
  // Called when the program needs to save its state. Use
  // this method to save your state to the Storage field
  // or some other means. 
  // 
  // This method is optional and can be removed if not
  // needed.

  Storage = userBlastFurniceGrp + ";"
    + userSourceCargoGrp + ";"
    + userMasterAssembler + ";"
    + userAssemblerInfo + ";";
  for (int i = 0; i < myIngots.Length; i++)
    Storage += ingotAmountTresholds[i].ToString() + ";";
  
}


public void Main(string argument) {
  // The main entry point of the script, invoked every time
  // one of the programmable block's Run actions are invoked.
  // 
  // The method itself is required, but the argument above
  // can be removed if not needed.

  // debug string
  string debugInfo = "Status der Montageanlage:\n";

  // reading command arguments
  string[] args = argument.Split(';');
  switch (args[0])
  {
    case "init":
      readCustomData();
      if(bInitialized)
        Echo("init done");
      else
        Echo("init failed");
      return;  // program done
    case "move":
      if (args.Length > 4)
      {
        moveAllItemsFromTo(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]));
        Echo("moved");
      }
      else
        Echo("invalid argument count");
      return;  // program done
    default:
      if (!bInitialized)
      {
        Echo("not initialized");
        return;
      }
      break;  // continue normal run
  }// switch

  Echo("run");

  List<IMyRefinery> furnices = new List<IMyRefinery>();
  List<IMyCargoContainer> containers = new List<IMyCargoContainer>();
  GridTerminalSystem.GetBlockGroupWithName(userBlastFurniceGrp).GetBlocksOfType<IMyRefinery>(furnices);
  GridTerminalSystem.GetBlockGroupWithName(userSourceCargoGrp).GetBlocksOfType<IMyCargoContainer>(containers);
  IMyTerminalBlock asblr = GridTerminalSystem.GetBlockWithName(userMasterAssembler);

  // transfer furnices content to containers
  for(int i = 0; i<furnices.Count;i++)
  {
    // Does furnice have refined ingots?
    if (furnices[i].GetInventory(1).IsItemAt(0))
    {
      // find next free container
      string dstName = "";
      for (int j = 0; j < containers.Count; j++)
      {
        if (!containers[j].GetInventory(0).IsFull)
        {
          dstName = containers[j].CustomName;
          break;
        }
      }// for containers.Count
      moveAllItemsFromTo(furnices[i].CustomName, dstName, 1);
    }
  }// for furnices.Count

  
  //IMyInventory inv = GridTerminalSystem.GetBlockWithName(userMasterAssembler).GetInventory(0);
  //inv.ContainItems((VRage.ObjectBuilders.SerializableDefinitionId)25);

  // collect all inventories of cargo group
  List<IMyInventory> inventories = new List<IMyInventory>();
  for(int i=0;i<containers.Count;i++)
    inventories.Add(containers[i].GetInventory(0));
  
  // sum all current ingots
  float[] dst_amounts = sumIngotAmountsIn(asblr.GetInventory(0));
  float[] src_amounts = sumIngotAmountsIn(inventories);

  // go trough every kind of ingot
  float difference, amount, quantity;
  for (int i = 0; i < myIngots.Length; i++)
  {
    difference = ingotAmountTresholds[i] - dst_amounts[i];
    if (difference > 0) // something is needed
    {
      if (src_amounts[i] > 0) // and it is available
      {
        List<int> positions = new List<int>();
        int k = -1;
        amount = 0;
        do
        {
          k++;
          amount = itemAmountIn(containers[k].GetInventory(0), ingotDefinitions[i], positions);
        } while (amount == 0 && k < containers.Count);

        // transfer a quantity of available ingot
        quantity = amount - difference;
        if (quantity < 0)
        {// more needed than it is in this container
          if (amount > 0)
            containers[k].GetInventory(0).TransferItemTo(asblr.GetInventory(0), positions[0], null, true, (VRage.MyFixedPoint)amount);
          
        }else // amount more or same as demanded, where diff > 0
          containers[k].GetInventory(0).TransferItemTo(asblr.GetInventory(0), positions[0], null, true, (VRage.MyFixedPoint)difference);
      } else // src_amounts
        debugInfo += "Bedarf an " + myIngots[i] + ": " + difference + "g\n";
    }// diff > 0

    // restock ingots
    if (src_amounts[i] < ingotAmountTresholds[i])
      debugInfo += "Vorrat auffüllen: " + myIngots[i] + "\n";
  }

  outputToLCD("AssemblerInfo", debugInfo);
}// main

}// class
}// namespace
