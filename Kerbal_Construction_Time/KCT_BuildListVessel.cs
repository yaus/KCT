﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.IO;

namespace KerbalConstructionTime
{
    public class KCT_BuildListVessel : IKCTBuildItem
    {
        private ShipConstruct ship;
        public double progress, buildPoints;
        public String launchSite, flag, shipName;
        public ListType type;
        public enum ListType { VAB, SPH, TechNode, Reconditioning, KSC };
        //public List<string> InventoryParts;
        public Dictionary<string, int> InventoryParts;
        public ConfigNode shipNode;
        public Guid id;
        public bool cannotEarnScience;
        public float cost = 0, TotalMass = 0, DistanceFromKSC = 0;
        public double buildRate { get { return KCT_Utilities.GetBuildRate(this); } }
        public double timeLeft
        {
            get
            {
                if (buildRate > 0)
                    return (buildPoints-progress)/buildRate;
                else
                    return double.PositiveInfinity;
            }
        }
        public List<Part> ExtractedParts { 
            get 
            { 
                List<Part> temp = new List<Part>();
                foreach (PseudoPart PP in this.GetPseudoParts())
                {
                    Part p = KCT_Utilities.GetAvailablePartByName(PP.name).partPrefab;
                    p.craftID = PP.uid;
                    temp.Add(p);
                }
                return temp;
            } 
        }
        public List<ConfigNode> ExtractedPartNodes
        {
            get
            {
                return this.shipNode.GetNodes("PART").ToList();
            }
        }
        public bool isFinished { get { return progress >= buildPoints; } }
        public KCT_KSC KSC { get { 
            return KCT_GameStates.KSCs.FirstOrDefault(k => ( type == ListType.VAB ? (
                k.VABList.FirstOrDefault(s => s.id == this.id) != null || k.VABWarehouse.FirstOrDefault(s => s.id == this.id) != null)
            : (k.SPHList.FirstOrDefault(s => s.id == this.id) != null || k.SPHWarehouse.FirstOrDefault(s => s.id == this.id) != null))); 
        } }

        public KCT_BuildListVessel(ShipConstruct s, String ls, double bP, String flagURL)
        {
            ship = s;
            shipNode = s.SaveShip();
            shipName = s.shipName;
            //Get total ship cost
            float dry, fuel;
            s.GetShipCosts(out dry, out fuel);
            cost = dry + fuel;
            TotalMass = 0;
            foreach (Part p in s.Parts)
            {
                TotalMass += p.mass;
                TotalMass += p.GetResourceMass();
            }

            launchSite = ls;
            buildPoints = bP;
            progress = 0;
            flag = flagURL;
            if (launchSite == "LaunchPad")
                type = ListType.VAB;
            else
                type = ListType.SPH;
            InventoryParts = new Dictionary<string, int>();
            id = Guid.NewGuid();
            cannotEarnScience = false;
        }

        public KCT_BuildListVessel(String name, String ls, double bP, String flagURL, float spentFunds)
        {
            ship = new ShipConstruct();
            launchSite = ls;
            shipName = name;
            buildPoints = bP;
            progress = 0;
            flag = flagURL;
            if (launchSite == "LaunchPad")
                type = ListType.VAB;
            else
                type = ListType.SPH;
            InventoryParts = new Dictionary<string, int>();
            cannotEarnScience = false;
            cost = spentFunds;
        }

        //private ProtoVessel recovered;

        public KCT_BuildListVessel(Vessel vessel) //For recovered vessels
        {
           /* if (KCT_GameStates.recoveryRequestVessel == null)
            {
                KCTDebug.Log("Somehow tried to recover something that was null!");
                return;
            }*/
            

            id = Guid.NewGuid();
            shipName = vessel.vesselName;
            shipNode = FromInFlightVessel(vessel);

            cost = KCT_Utilities.GetTotalVesselCost(shipNode);
            TotalMass = 0;
            InventoryParts = new Dictionary<string, int>();
            foreach (ProtoPartSnapshot p in vessel.protoVessel.protoPartSnapshots)
            {
                //InventoryParts.Add(p.partInfo.name + KCT_Utilities.GetTweakScaleSize(p));
                string name = p.partInfo.name;
                int amt = 1;
                if (KCT_Utilities.PartIsProcedural(p))
                {
                    float dry, wet;
                    ShipConstruction.GetPartCosts(p, p.partInfo, out dry, out wet);
                    amt = (int)(1000 * dry);
                }
                else
                {
                    name += KCT_Utilities.GetTweakScaleSize(p);
                }
                KCT_Utilities.AddToDict(InventoryParts, name, amt);

                TotalMass += p.mass;
                foreach (ProtoPartResourceSnapshot rsc in p.resources)
                {
                    PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(rsc.resourceName);
                    if (def != null)
                        TotalMass += def.density * float.Parse(rsc.resourceValues.GetValue("amount"));
                }
            }
            cannotEarnScience = true;

            buildPoints = KCT_Utilities.GetBuildTime(shipNode.GetNodes("PART").ToList(), true, InventoryParts);
            flag = HighLogic.CurrentGame.flagURL;
            progress = buildPoints;

            DistanceFromKSC = (float)SpaceCenter.Instance.GreatCircleDistance(SpaceCenter.Instance.cb.GetRelSurfaceNVector(vessel.latitude, vessel.longitude));
        }

        private ConfigNode FromInFlightVessel(Vessel VesselToSave)
        {
            //This code is taken from InflightShipSave by Claw, using the CC-BY-NC-SA license.
            //This code thus is licensed under the same license, despite the GPLv3 license covering original KCT code
            //See https://github.com/ClawKSP/InflightShipSave

            string ShipName = VesselToSave.vesselName;
           // Debug.LogWarning("Saving: " + ShipName);

            ShipConstruct ConstructToSave = new ShipConstruct(ShipName, "", VesselToSave.parts[0]);

            Quaternion OriginalRotation = VesselToSave.vesselTransform.rotation;
            Vector3 OriginalPosition = VesselToSave.vesselTransform.position;

            VesselToSave.SetRotation(new Quaternion(0, 0, 0, 1));
            Vector3 ShipSize = ShipConstruction.CalculateCraftSize(ConstructToSave);
            VesselToSave.SetPosition(new Vector3(0, ShipSize.y + 2, 0));

            ConfigNode CN = new ConfigNode("ShipConstruct");
            CN = ConstructToSave.SaveShip();
            SanitizeShipNode(CN);

            VesselToSave.SetRotation(OriginalRotation);
            VesselToSave.SetPosition(OriginalPosition);
            //End of Claw's code. Thanks Claw!
            return CN;
        }

        private ConfigNode SanitizeShipNode(ConfigNode node)
        {
            //PART, MODULE -> clean experiments, repack chutes, disable engines
            String filePath = KSPUtil.ApplicationRootPath + "GameData/KerbalConstructionTime/KCT_ModuleTemplates.cfg";
            if (!File.Exists(filePath))
            {
                CreateInitialTemplates();
            }
            ConfigNode ModuleTemplates = ConfigNode.Load(filePath);
            ConfigNode[] templates = ModuleTemplates.GetNodes("MODULE");
            foreach(ConfigNode part in node.GetNodes("PART"))
            {
                foreach(ConfigNode module in part.GetNodes("MODULE"))
                {
                    SanitizeNode(module, templates);
                }
            }
            return node;
        }

        private void SanitizeNode(ConfigNode module, ConfigNode[] templates)
        {
            string name = module.GetValue("name");

            if (module.HasNode("ScienceData"))
            {
                module.RemoveNodes("ScienceData");
            }
            if (name == "Log")
                module.ClearValues();

            ConfigNode template = templates.FirstOrDefault(t => t.GetValue("name") == name);
            if (template == null) return;
            ConfigNode.ValueList values = template.values;
            foreach (ConfigNode.Value val in values)
            {
                module.SetValue(val.name, val.value);
            }

            foreach (ConfigNode node in template.GetNodes()) //This should account for nested nodes, like RealChutes' PARACHUTE node
            {
                if (module.HasNode(node.name))
                {
                    foreach (ConfigNode.Value val in node.values)
                        module.GetNode(node.name).SetValue(val.name, val.value);
                }
            }

            foreach (ConfigNode node in module.GetNodes("MODULE"))
                SanitizeNode(node, templates);

            
            /*
            if (name.Contains("ModuleEngines"))
            {
                module.SetValue("staged", "False");
                module.SetValue("flameout", "False");
                module.SetValue("EngineIgnited", "False");
                module.SetValue("engineShutdown", "False");
                module.SetValue("currentThrottle", "0");
                module.SetValue("manuallyOverridden", "False");
            }
            else if (name == "ModuleScienceExperiment")
            {
                module.SetValue("Deployed", "False");
                module.SetValue("Inoperable", "False");
            }
            else if (name == "ModuleParachute")
            {
                module.SetValue("staged", "False");
                module.SetValue("persistentState", "STOWED");
            }
            else if (name == "Log")
            {
                module.ClearValues();
            }

            if (module.HasNode("ScienceData"))
            {
                module.RemoveNodes("ScienceData");
            }
            */
            
        }

        private void CreateInitialTemplates()
        {
            ConfigNode templates = new ConfigNode("KCT_ModuleTemplates");
            ConfigNode module;
            
            //ModuleEngines
            module = new ConfigNode("MODULE");
            module.AddValue("name", "ModuleEngines");
            module.AddValue("staged", "False");
            module.AddValue("flameout", "False");
            module.AddValue("EngineIgnited", "False");
            module.AddValue("engineShutdown", "False");
            module.AddValue("currentThrottle", "0");
            module.AddValue("manuallyOverridden", "False");
            templates.AddNode(module);

            //ModuleEnginesFX
            module = new ConfigNode("MODULE");
            module.AddValue("name", "ModuleEnginesFX");
            module.AddValue("staged", "False");
            module.AddValue("flameout", "False");
            module.AddValue("EngineIgnited", "False");
            module.AddValue("engineShutdown", "False");
            module.AddValue("currentThrottle", "0");
            module.AddValue("manuallyOverridden", "False");
            templates.AddNode(module);

            //ModuleScienceExperiment
            module = new ConfigNode("MODULE");
            module.AddValue("name", "ModuleScienceExperiment");
            module.AddValue("Deployed", "False");
            module.AddValue("Inoperable", "False");
            templates.AddNode(module);

            //ModuleParachute
            module = new ConfigNode("MODULE");
            module.AddValue("name", "ModuleParachute");
            module.AddValue("staged", "False");
            module.AddValue("persistentState", "STOWED");
            templates.AddNode(module);

            //RealChuteModule
            module = new ConfigNode("MODULE");
            module.AddValue("name", "RealChuteModule");
            module.AddValue("armed", "False");
            module.AddValue("staged", "False");
            module.AddValue("launched", "False");
            module.AddValue("oneWasDeployed", "False");
            ConfigNode PARACHUTE = new ConfigNode("PARACHUTE");
            PARACHUTE.AddValue("capOff", "False");
            PARACHUTE.AddValue("time", "0");
            PARACHUTE.AddValue("depState", "STOWED");
            module.AddNode(PARACHUTE);
            templates.AddNode(module);


            templates.Save(KSPUtil.ApplicationRootPath + "GameData/KerbalConstructionTime/KCT_ModuleTemplates.cfg");
        }

        public KCT_BuildListVessel NewCopy(bool RecalcTime)
        {
            KCT_BuildListVessel ret = new KCT_BuildListVessel(this.shipName, this.launchSite, this.buildPoints, this.flag, this.cost);
            ret.shipNode = this.shipNode.CreateCopy();
            ret.id = Guid.NewGuid();
            if (RecalcTime)
            {
                ret.buildPoints = KCT_Utilities.GetBuildTime(ret.ExtractedPartNodes, true, this.InventoryParts.Count > 0);
            }
            ret.TotalMass = this.TotalMass;
            return ret;
        }

        public ShipConstruct GetShip()
        {
            if (ship != null && ship.Parts != null && ship.Parts.Count > 0) //If the parts are there, then the ship is loaded
            {
                return ship;
            }
            else if (shipNode != null) //Otherwise load the ship from the ConfigNode
            {
                ship.LoadShip(shipNode);
            }
            return ship;
        }

        public void Launch()
        {
            KCT_GameStates.flightSimulated = false;
            string tempFile = KSPUtil.ApplicationRootPath + "saves/" + HighLogic.SaveFolder + "/Ships/temp.craft";
            UpdateRFTanks();
            shipNode.Save(tempFile);
            FlightDriver.StartWithNewLaunch(tempFile, flag, launchSite, new VesselCrewManifest());
            KCT_GameStates.LaunchFromTS = false;
        }

        private void UpdateRFTanks()
        {
            foreach (ConfigNode cn in shipNode.GetNodes("PART"))
            {
                foreach (ConfigNode module in cn.GetNodes("MODULE"))
                {
                    if (module.GetValue("name") == "ModuleFuelTanks")
                    {
                        if (module.HasValue("timestamp"))
                        {
                            KCTDebug.Log("Updating RF timestamp on a part");
                            module.SetValue("timestamp", Planetarium.GetUniversalTime().ToString());
                        }
                    }
                }
            }
        }

        //NOTE: This is an approximation. This won't properly take into account for resources and tweakscale! DO NOT USE IF YOU CARE 100% ABOUT THE MASS
        public double GetTotalMass()
        {
            if (TotalMass != 0) return TotalMass;
            double mass = 0;
            foreach (ConfigNode p in this.ExtractedPartNodes)
            {
                float n1, n2;
                mass += ShipConstruction.GetPartTotalMass(p, KCT_Utilities.GetAvailablePartByName(KCT_Utilities.PartNameFromNode(p)), out n1, out n2);
            }
            return mass;
        }

        public double GetTotalCost()
        {
            if (cost != 0) return cost;
            return KCT_Utilities.GetTotalVesselCost(shipNode);
        }

        public bool RemoveFromBuildList()
        {
            string typeName="";
            bool removed = false;
            KCT_KSC theKSC = this.KSC;
            if (theKSC == null)
            {
                KCTDebug.Log("Could not find the KSC to remove vessel!");
                return false;
            }
            if (type == ListType.SPH)
            {
                if (theKSC.SPHWarehouse.Contains(this))
                    removed = theKSC.SPHWarehouse.Remove(this);
                else if (theKSC.SPHList.Contains(this))
                    removed = theKSC.SPHList.Remove(this);
                typeName="SPH";
            }
            else if (type == ListType.VAB)
            {
                if (theKSC.VABWarehouse.Contains(this))
                    removed = theKSC.VABWarehouse.Remove(this);
                else if (theKSC.VABList.Contains(this))
                    removed = theKSC.VABList.Remove(this);
                typeName="VAB";
            }
            KCTDebug.Log("Removing " + shipName + " from "+ typeName +" storage/list.");
            if (!removed)
            {
                KCTDebug.Log("Failed to remove ship from list! Performing direct comparison of ids...");
                foreach (KCT_BuildListVessel blv in theKSC.SPHWarehouse)
                {
                    if (blv.id == this.id)
                    {
                        KCTDebug.Log("Ship found in SPH storage. Removing...");
                        removed = theKSC.SPHWarehouse.Remove(blv);
                        break;
                    }
                }
                if (!removed)
                {
                    foreach (KCT_BuildListVessel blv in theKSC.VABWarehouse)
                    {
                        if (blv.id == this.id)
                        {
                            KCTDebug.Log("Ship found in VAB storage. Removing...");
                            removed = theKSC.VABWarehouse.Remove(blv);
                            break;
                        }
                    }
                }
                if (!removed)
                {
                    foreach (KCT_BuildListVessel blv in theKSC.VABList)
                    {
                        if (blv.id == this.id)
                        {
                            KCTDebug.Log("Ship found in VAB List. Removing...");
                            removed = theKSC.VABList.Remove(blv);
                            break;
                        }
                    }
                }
                if (!removed)
                {
                    foreach (KCT_BuildListVessel blv in theKSC.SPHList)
                    {
                        if (blv.id == this.id)
                        {
                            KCTDebug.Log("Ship found in SPH list. Removing...");
                            removed = theKSC.SPHList.Remove(blv);
                            break;
                        }
                    }
                }
            }
            if (removed) KCTDebug.Log("Sucessfully removed ship from storage.");
            else KCTDebug.Log("Still couldn't remove ship!");
            return removed;
        }

        public List<PseudoPart> GetPseudoParts()
        {
            List<PseudoPart> retList = new List<PseudoPart>();
            ConfigNode[] partNodes = shipNode.GetNodes("PART");
            // KCTDebug.Log("partNodes count: " + partNodes.Length);

            foreach (ConfigNode CN in partNodes)
            {
                string name = CN.GetValue("part");
                string pID = "";
                if (name != null)
                {
                    string[] split = name.Split('_');
                    name = split[0];
                    pID = split[1];
                }
                else
                {
                    name = CN.GetValue("name");
                    pID = CN.GetValue("uid");
                }
                
                //for (int i = 0; i < split.Length - 1; i++)
                //    pName += split[i];
                PseudoPart returnPart = new PseudoPart(name, pID);
                retList.Add(returnPart);
            }
            return retList;
        }

        public double AddProgress(double toAdd)
        {
            progress+=toAdd;
            return progress;
        }

        public double ProgressPercent()
        {
            return 100 * (progress / buildPoints);
        }

        string IKCTBuildItem.GetItemName()
        {
            return this.shipName;
        }

        double IKCTBuildItem.GetBuildRate()
        {
            return this.buildRate;
        }

        double IKCTBuildItem.GetTimeLeft()
        {
            return this.timeLeft;
        }

        ListType IKCTBuildItem.GetListType()
        {
            return this.type;
        }

        bool IKCTBuildItem.IsComplete()
        {
            return (progress >= buildPoints);
        }

    }

    public class PseudoPart
    {
        public string name;
        public uint uid;
        
        public PseudoPart(string PartName, uint ID)
        {
            name = PartName;
            uid = ID;
        }

        public PseudoPart(string PartName, string ID)
        {
            name = PartName;
            uid = uint.Parse(ID);
        }
    }
}
/*
Copyright (C) 2014  Michael Marvin, Zachary Eck

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/