﻿using Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SVSModel
{
    class MineralNCalculations
    {
        /// <summary>
        /// Calculates soil mineral nitrogen from an assumed initial value and modeled crop uptake and mineralisation from residues and soil organic matter
        /// </summary>
        /// <param name="simDates">series of dates over the duration of the simulation</param>
        /// <param name="initialN">assumed mineral at the start of the first crop in the rotation</param>
        /// <param name="uptake">series of daily N uptake values over the duration of the rotatoin</param>
        /// <param name="residue">series of mineral N released daily to the soil from residue mineralisation</param>
        /// <param name="som">series of mineral N released daily to the soil from organic matter</param>
        /// <returns>date indexed series of estimated soil mineral N content</returns>
        public static Dictionary<DateTime, double> Initial(DateTime[] simDates, double initialN, Dictionary<DateTime, double> uptake,
                                                                    Dictionary<DateTime, double> residue, Dictionary<DateTime, double> som)
        {
            Dictionary<DateTime, double> minN = Functions.dictMaker(simDates, new double[simDates.Length]);
            foreach (DateTime d in simDates)
            {
                if (d == simDates[0])
                {
                    minN[simDates[0]] = initialN;
                }
                else
                {
                    minN[d] = minN[d.AddDays(-1)];
                }
                minN[d] += residue[d];
                minN[d] += som[d];
                double actualUptake = uptake[d]; //Math.Min(uptake[d], minN[d]);
                minN[d] -= actualUptake;
            }
            return minN;
        }

        /// <summary>
        /// Takes soil mineral N test values and adjustes to predicted N balance to correspond with these values on their specific dates
        /// </summary>
        /// <param name="testResults">date indexed series of test results</param>
        /// <param name="soilN">date indexed series of soil mineral N estimates to be corrected with measurements.  Passed in as ref so 
        /// the corrections are applied to the property passed in</param>
        public static void TestCorrection(Dictionary<DateTime, 
                                          double> testResults, 
                                          ref Dictionary<DateTime, 
                                          double> soilN)
        {
            foreach (DateTime d in testResults.Keys)
            {
                double correction = testResults[d] - soilN[d];
                DateTime[] simDatesToCorrect = Functions.DateSeries(d, soilN.Keys.Last());
                foreach (DateTime c in simDatesToCorrect)
                {
                    soilN[c] += correction;
                }
            }
        }

        /// <summary>
        /// Adds specified establishment fert to the soil N then determines how much additional fertiliser N is required and when the crop will need it.
        /// </summary>
        /// <param name="soilN">Date indexed series of soil N corrected for test values, passed as ref so scheduled fertiliser is added to this property</param>
        /// <param name="residueMin">Date indexed series of daily mineralisation from residues</param>
        /// <param name="somN">Date indexed series of daily mineralisation from soil organic matter</param>
        /// <param name="cropN">Date indexed series of standing crop N</param>
        /// <param name="testResults">Date indexed set of test values</param>
        /// <param name="config">A specific class that holds all the simulation configuration data in the correct types for use in the model</param>
        /// <returns></returns>
        public static void DetermineFertRequirements(ref Dictionary<DateTime, double> fert,
                                                     ref Dictionary<DateTime, double> soilN, 
                                                     ref Dictionary<DateTime, double> lostN, 
                                                     Dictionary<DateTime, double> residueMin,
                                                     Dictionary<DateTime, double> somN, 
                                                     Dictionary<DateTime, double> cropN,
                                                     Dictionary<DateTime, double> testResults, 
                                                     Config config)
        {
            //Make all the necessary data structures
            DateTime[] cropDates = Functions.DateSeries(config.Current.EstablishDate, config.Current.HarvestDate);
            DateTime startSchedulleDate = config.Current.EstablishDate; //Earliest start to schedulling is establishment date
            if (testResults.Keys.Count > 0)
                startSchedulleDate = testResults.Keys.Last(); //If test results specified after establishment that becomes start of schedulling date
            DateTime lastFertDate = new DateTime();
            foreach (DateTime d in fert.Keys)
            {
                if (fert[d] > 0)
                    lastFertDate = d;
            }
            if (lastFertDate > startSchedulleDate)
                startSchedulleDate = lastFertDate;  //If Fertiliser already applied after last test date them last fert date becomes start of scheudlling date
            startSchedulleDate = startSchedulleDate.AddDays(1); //Start schedule the day after the last test or application
            DateTime[] schedullingDates = Functions.DateSeries(startSchedulleDate, config.Current.HarvestDate);

            //Calculate total N from mineralisatin over the duration of the crop
            double mineralisation = 0;
            double fertToDate = 0;
            foreach (DateTime d in schedullingDates)
            {
                mineralisation += residueMin[d];
                mineralisation += somN[d];
                fertToDate += fert[d];
            }

            // Set other variables needed to derive fertiliser requirement
            double CropN = cropN[config.Current.HarvestDate] - cropN[startSchedulleDate];
            double trigger = config.field.Trigger;
            double efficiency = config.field.Efficiency;

            // Calculate total fertiliser requirement and ammount to be applied at each application
            double NFertReq = (CropN + trigger) - soilN[startSchedulleDate] - mineralisation - fertToDate ;
            NFertReq = Math.Max(0,NFertReq * 1 / efficiency);
            int splits = config.field.Splits;
            double NAppn = Math.Ceiling(NFertReq / splits);

            // Determine dates that each fertiliser application should be made
            double FertApplied = 0;
            if (splits > 0)
            {
                foreach (DateTime d in schedullingDates)
                {
                    if ((soilN[d] < trigger) && (FertApplied < NFertReq))
                    {
                        AddFertiliser(ref soilN, NAppn * efficiency, d, config);
                        fert[d] += NAppn;
                        FertApplied += NAppn;
                        lostN[d] = NAppn * (1 - efficiency);
                    }
                }
            }
        }

        public static Dictionary<DateTime, double> ApplyExistingFertiliser(DateTime[] simDates, 
                                                                           Dictionary<DateTime, double> nApplied,
                                                                           Dictionary<DateTime, double> testResults, 
                                                                           ref Dictionary<DateTime, double> soilN,
                                                                           ref Dictionary<DateTime, double> lostN,
                                                                           Config config)
        {
            Dictionary<DateTime, double> fert = Functions.dictMaker(simDates, new double[simDates.Length]);
            DateTime startApplicationDate = config.Current.EstablishDate; //Earliest start to schedulling is establishment date
            if (testResults.Keys.Count > 0)
                startApplicationDate = testResults.Keys.Last(); //If test results specified after establishment that becomes start of schedulling date
            startApplicationDate = startApplicationDate.AddDays(1); //Start schedule the day after the last test or application
            double efficiency = config.field.Efficiency;
            foreach (DateTime d in nApplied.Keys)
            {
                if (d > startApplicationDate)
                {
                    AddFertiliser(ref soilN, nApplied[d] * efficiency, d, config);
                }
                fert[d] = nApplied[d];
                lostN[d] = (1 - efficiency);
            }
            return fert;
        }

        /// <summary>
        /// function to update series of soil mineral N for dates following N fertiliser application
        /// </summary>
        /// <param name="soilN">Date indexed series of soil mineral N data </param>
        /// <param name="fertN">Amount of fertiliser to apply</param>
        /// <param name="fertDate">Date to apply fertiliser</param>
        /// <param name="config">A specific class that holds all the simulation configuration data in the correct types for use in the model</param>
        public static void AddFertiliser(ref Dictionary<DateTime, double> soilN, double fertN, DateTime fertDate, Config config)
        {
            DateTime[] datesFollowingFert = Functions.DateSeries(fertDate, config.Following.HarvestDate);
            foreach (DateTime d in datesFollowingFert)
            {
                soilN[d] += fertN;
            }
        }
    }
}