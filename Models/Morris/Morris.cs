﻿namespace Models
{
    using APSIM.Shared.Utilities;
    using Models.Core;
    using Models.Factorial;
    using Models.Interfaces;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Serialization;
    using Utilities;

    /// <summary>
    /// # [Name]
    /// Encapsulates a Morris analysis.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.DualGridView")]
    [PresenterName("UserInterface.Presenters.TablePresenter")]
    [ValidParent(ParentType = typeof(Simulations))]
    public class Morris : Model, ISimulationGenerator, ICustomDocumentation, IModelAsTable, IPostSimulationTool
    {
        /// <summary>A list of factors that we are to run</summary>
        private List<List<FactorValue>> allCombinations = new List<List<FactorValue>>();

        /// <summary>A number of the currently running sim</summary>
        private int simulationNumber;

        /// <summary>Parameter values coming back from R</summary>
        public DataTable ParameterValues { get; set; }

        /// <summary>The numebr of paths to run</summary>
        public int NumPaths { get; set; } = 200;

        /// <summary>
        /// List of parameters
        /// </summary>
        /// <remarks>
        /// Needs to be public so that it gets written to .apsimx file
        /// </remarks>
        public List<Parameter> Parameters { get; set; }

        /// <summary>
        /// List of years
        /// </summary>
        /// <remarks>
        /// Needs to be public so that it gets written to .apsimx file
        /// </remarks>
        public int[] Years { get; set; }

        /// <summary>List of simulation names from last run</summary>
        [XmlIgnore]
        public List<string> simulationNames { get; set; }

        /// <summary>Constructor</summary>
        public Morris()
        {
            Parameters = new List<Parameter>();
            allCombinations = new List<List<FactorValue>>();
            simulationNames = new List<string>();
        }

        /// <summary>
        /// Gets or sets the table of values.
        /// </summary>
        [XmlIgnore]
        public List<DataTable> Tables
        {
            get
            {
                List<DataTable> tables = new List<DataTable>();

                // Add a constant table.
                DataTable constant = new DataTable();
                constant.Columns.Add("Property", typeof(string));
                constant.Columns.Add("Value", typeof(int));
                DataRow constantRow = constant.NewRow();
                constantRow["Property"] = "Number of paths:";
                constantRow["Value"] = NumPaths;
                constant.Rows.Add(constantRow);
                tables.Add(constant);

                // Add a parameter table
                DataTable table = new DataTable();
                table.Columns.Add("Name", typeof(string));
                table.Columns.Add("Path", typeof(string));
                table.Columns.Add("LowerBound", typeof(double));
                table.Columns.Add("UpperBound", typeof(double));

                foreach (Parameter param in Parameters)
                {
                    DataRow row = table.NewRow();
                    row["Name"] = param.Name;
                    row["Path"] = param.Path;
                    row["LowerBound"] = param.LowerBound;
                    row["UpperBound"] = param.UpperBound;
                    table.Rows.Add(row);
                }
                tables.Add(table);

                return tables;
            }
            set
            {
                NumPaths = Convert.ToInt32(value[0].Rows[0][1]);
                
                Parameters.Clear();
                foreach (DataRow row in value[1].Rows)
                {
                    Parameter param = new Parameter();
                    if (!Convert.IsDBNull(row["Name"]))
                        param.Name = row["Name"].ToString();
                    if (!Convert.IsDBNull(row["Path"]))
                        param.Path = row["Path"].ToString();
                    if (!Convert.IsDBNull(row["LowerBound"]))
                        param.LowerBound = Convert.ToDouble(row["LowerBound"]);
                    if (!Convert.IsDBNull(row["UpperBound"]))
                        param.UpperBound = Convert.ToDouble(row["UpperBound"]);
                    if (param.Name != null || param.Path != null)
                        Parameters.Add(param);
                }
            }
        }

        private Stream serialisedBase;
        private Simulations parentSimulations;

        /// <summary>Simulation runs are about to begin.</summary>
        [EventSubscribe("BeginRun")]
        private void OnBeginRun()
        {
            Initialise();
            simulationNumber = 1;
        }

        /// <summary>Gets the next job to run</summary>
        public Simulation NextSimulationToRun(bool fullFactorial = true)
        {
            if (allCombinations.Count == 0)
                return null;

            var combination = allCombinations[0];
            allCombinations.RemoveAt(0);

            Simulation newSimulation = Apsim.DeserialiseFromStream(serialisedBase) as Simulation;
            newSimulation.Name = "Simulation" + simulationNumber;
            newSimulation.Parent = null;
            newSimulation.FileName = parentSimulations.FileName;
            Apsim.ParentAllChildren(newSimulation);

            // Make substitutions.
            parentSimulations.MakeSubsAndLoad(newSimulation);

            foreach (FactorValue value in combination)
                value.ApplyToSimulation(newSimulation);

            PushFactorsToReportModels(newSimulation, combination);

            simulationNumber++;
            return newSimulation;
        }

        /// <summary>Find all report models and give them the factor values.</summary>
        /// <param name="factorValues">The factor values to send to each report model.</param>
        /// <param name="simulation">The simulation to search for report models.</param>
        private void PushFactorsToReportModels(Simulation simulation, List<FactorValue> factorValues)
        {
            List<string> names = new List<string>();
            List<string> values = new List<string>();
            names.Add("SimulationName");
            values.Add(simulation.Name);

            foreach (FactorValue factor in factorValues)
            {
                names.Add(factor.Name);
                values.Add(factor.Values[0].ToString());
            }

            foreach (Report.Report report in Apsim.ChildrenRecursively(simulation, typeof(Report.Report)))
            {
                report.ExperimentFactorNames = names;
                report.ExperimentFactorValues = values;
            }
        }

        /// <summary>
        /// Generates an .apsimx file for each simulation in the experiment and returns an error message (if it fails).
        /// </summary>
        /// <param name="path">Full path including filename and extension.</param>
        /// <returns>Empty string if successful, error message if it fails.</returns>
        public void GenerateApsimXFile(string path)
        {
            Simulation sim = NextSimulationToRun();
            while (sim != null)
            {
                Simulations sims = Simulations.Create(new List<IModel> { sim, new Models.Storage.DataStore() });

                string xml = Apsim.Serialise(sims);
                File.WriteAllText(Path.Combine(path, sim.Name + ".apsimx"), xml);
                sim = NextSimulationToRun();
            }
        }

        /// <summary>Gets a list of simulation names</summary>
        public IEnumerable<string> GetSimulationNames(bool fullFactorial = true)
        {
            return simulationNames;
        }

        /// <summary>Gets a list of factors</summary>
        public List<ISimulationGeneratorFactors> GetFactors()
        {
            string[] columnNames = new string[] { "Param", "Year" };
            string[] columnValues = new string[2];

            var factors = new List<ISimulationGeneratorFactors>();
            foreach (Parameter param in Parameters)
            {
                foreach (var year in Years)
                {
                    factors.Add(new SimulationGeneratorFactors(columnNames, new string[] { param.Name, year.ToString() },
                                                               "ParameterxYear", param.Name + year));
                    factors.Add(new SimulationGeneratorFactors(new string[] { "Year" } , new string[] { year.ToString() },
                                                               "Year", year.ToString()));
                }
                factors.Add(new SimulationGeneratorFactors(new string[] { "Param" }, new string[] { param.Name },
                                                           "Parameter", param.Name));
            }
            return factors;
        }

        /// <summary>
        /// Initialise the experiment ready for creating simulations.
        /// </summary>
        private void Initialise()
        {
            parentSimulations = Apsim.Parent(this, typeof(Simulations)) as Simulations;
            Simulation baseSimulation = Apsim.Child(this, typeof(Simulation)) as Simulation;
            serialisedBase = Apsim.SerialiseToStream(baseSimulation) as Stream;
            allCombinations.Clear();
            CalculateFactors();
        }

        /// <summary>
        /// Calculate factors that we need to run. Put combinations into allCombinations
        /// </summary>
        private void CalculateFactors()
        {
            if (allCombinations.Count == 0)
            {
                ParameterValues = CalculateMorrisParameterValues();
                if (ParameterValues == null || ParameterValues.Rows.Count == 0)
                    throw new Exception("The morris function in R returned null");

                int simulationNumber = 1;
                simulationNames.Clear();
                foreach (DataRow parameterRow in ParameterValues.Rows)
                {
                    List<FactorValue> factors = new List<FactorValue>();
                    foreach (Parameter param in Parameters)
                    {
                        object value = Convert.ToDouble(parameterRow[param.Name]);
                        FactorValue f = new FactorValue(null, param.Name, param.Path, value);
                        factors.Add(f);
                    }

                    string newSimulationName = "Simulation" + simulationNumber;
                    simulationNames.Add(newSimulationName);
                    allCombinations.Add(factors);
                    simulationNumber++;
                }
            }
        }

        /// <summary>
        /// Get a list of parameter values that we are to run. Call R to do this.
        /// </summary>
        private DataTable CalculateMorrisParameterValues()
        {
            string script;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Models.Resources.Morris.R"))
                using (StreamReader reader = new StreamReader(s))
                    script = reader.ReadToEnd();

            script = script.Replace("%NUMPATHS%", NumPaths.ToString());
            script = script.Replace("%PARAMNAMES%", StringUtilities.Build(Parameters.Select(p => p.Name), ",", "\"", "\""));
            script = script.Replace("%PARAMLOWERS%", StringUtilities.Build(Parameters.Select(p => p.LowerBound), ","));
            script = script.Replace("%PARAMUPPERS%", StringUtilities.Build(Parameters.Select(p => p.UpperBound), ","));
            string rFileName = Path.GetTempFileName();
            File.WriteAllText(rFileName, script);
            R r = new R();
            Console.WriteLine(r.GetPackage("sensitivity"));
            return r.RunToTable(rFileName);
        }

        /// <summary>
        /// Get a list of morris values (ee, mustar, sigmastar) from R
        /// </summary>
        private DataTable CalculateMorrisValues()
        {
            string tempFileName = Path.GetTempFileName();

            string script = string.Format
                            ("T <- read.csv(\"{0}\"" + Environment.NewLine +
                             "DF <- as.data.frame(T)" + Environment.NewLine +
                             "APSIMMorris$X <- DF" + Environment.NewLine +
                             "tell(APSIMMorris)" + Environment.NewLine,
                             tempFileName);

            string rFileName = Path.GetTempFileName();
            File.WriteAllText(rFileName, script);
            R r = new R();
            Console.WriteLine(r.GetPackage("sensitivity"));
            return r.RunToTable(rFileName);
        }

        /// <summary>
        /// Gets the base simulation
        /// </summary>
        public Simulation BaseSimulation
        {
            get
            {
                return Apsim.Child(this, typeof(Simulation)) as Simulation;
            }
        }

        /// <summary>
        /// Create a specific simulation.
        /// </summary>
        public Simulation CreateSpecificSimulation(string name)
        {
            //List<List<FactorValue>> allCombinations = AllCombinations();
            //Simulation baseSimulation = Apsim.Child(this, typeof(Simulation)) as Simulation;
            //Simulations parentSimulations = Apsim.Parent(this, typeof(Simulations)) as Simulations;

            //foreach (List<FactorValue> combination in allCombinations)
            //{
            //    string newSimulationName = Name;
            //    foreach (FactorValue value in combination)
            //        newSimulationName += value.Name;

            //    if (newSimulationName == name)
            //    {
            //        Simulation newSimulation = Apsim.Clone(baseSimulation) as Simulation;
            //        newSimulation.Name = newSimulationName;
            //        newSimulation.Parent = null;
            //        newSimulation.FileName = parentSimulations.FileName;
            //        Apsim.ParentAllChildren(newSimulation);

            //        // Make substitutions.
            //        parentSimulations.MakeSubstitutions(newSimulation);

            //        // Connect events and links in our new  simulation.
            //        Events events = new Events(newSimulation);
            //        LoadedEventArgs loadedArgs = new LoadedEventArgs();
            //        events.Publish("Loaded", new object[] { newSimulation, loadedArgs });

            //        foreach (FactorValue value in combination)
            //            value.ApplyToSimulation(newSimulation);

            //        PushFactorsToReportModels(newSimulation, combination);

            //        return newSimulation;
            //    }
            //}

            return null;
        }

        /// <summary>
        /// Generates the name for a combination of FactorValues.
        /// </summary>
        /// <param name="factors"></param>
        /// <returns></returns>
        private string GetName(List<FactorValue> factors)
        {
            string str = Name;
            foreach (FactorValue factor in factors)
            {
                str += factor.Name;
            }
            return str;
        }

        /// <summary>Main run method for performing our post simulation calculations</summary>
        /// <param name="dataStore">The data store.</param>
        public void Run(IStorageReader dataStore)
        {
            DataTable predictedData = dataStore.GetData("Report");
            if (predictedData != null)
            {
                // Setup some file names
                string morrisParametersFileName = Path.Combine(Path.GetTempPath(), "parameters.csv");
                string apsimVariableFileName = Path.Combine(Path.GetTempPath(), "apsimvariable.csv");
                string eeFileName = Path.Combine(Path.GetTempPath(), "ee.csv");
                string statsFileName = Path.Combine(Path.GetTempPath(), "stats.csv");
                string rFileName = Path.Combine(Path.GetTempPath(), "script.r");

                // Determine how many years we have per simulation
                DataView view = new DataView(predictedData);
                view.RowFilter = "SimulationName='Simulation1'";
                Years = DataTableUtilities.GetColumnAsIntegers(view, "Clock.Today.Year");

                // Write parameters
                using (StreamWriter writer = new StreamWriter(morrisParametersFileName))
                    DataTableUtilities.DataTableToText(ParameterValues, 0, ",", true, writer);

                // Create a table of all predicted values
                DataTable predictedValues = new DataTable();

                List<string> descriptiveColumnNames = new List<string>();
                List<string> variableNames = new List<string>();
                foreach (double year in Years)
                {
                    view.RowFilter = "Clock.Today.Year=" + year;

                    foreach (DataColumn predictedColumn in view.Table.Columns)
                    {
                        if (predictedColumn.DataType == typeof(double))
                        {
                            double[] valuesForYear = DataTableUtilities.GetColumnAsDoubles(view, predictedColumn.ColumnName);
                            if (valuesForYear.Distinct().Count() == 1)
                            {
                                if (!descriptiveColumnNames.Contains(predictedColumn.ColumnName))
                                    descriptiveColumnNames.Add(predictedColumn.ColumnName);
                            }
                            else
                            {
                                DataTableUtilities.AddColumn(predictedValues, predictedColumn.ColumnName + year, valuesForYear);
                                if (!variableNames.Contains(predictedColumn.ColumnName))
                                    variableNames.Add(predictedColumn.ColumnName);
                            }
                        }
                    }
                }

                // write predicted values file
                using (StreamWriter writer = new StreamWriter(apsimVariableFileName))
                    DataTableUtilities.DataTableToText(predictedValues, 0, ",", true, writer);

                // write script
                string paramNames = StringUtilities.Build(Parameters.Select(p => p.Name), ",", "\"", "\"");
                string lowerBounds = StringUtilities.Build(Parameters.Select(p => p.LowerBound), ",");
                string upperBounds = StringUtilities.Build(Parameters.Select(p => p.UpperBound), ",");
                string script = string.Format
                                ("library('sensitivity')" + Environment.NewLine +
                                 "params <- c({0})" + Environment.NewLine +
                                 "apsimMorris<-morris(model=NULL" + Environment.NewLine +
                                 "    ,params #string vector of parameter names" + Environment.NewLine +
                                 "    ,{1} #no of paths within the total parameter space" + Environment.NewLine +
                                 "    ,design=list(type=\"oat\",levels=20,grid.jump=10)" + Environment.NewLine +
                                 "    ,binf=c({2}) #min for each parameter" + Environment.NewLine +
                                 "    ,bsup=c({3}) #max for each parameter" + Environment.NewLine +
                                 "    ,scale=T" + Environment.NewLine +
                                 "    )" + Environment.NewLine +
                                 "apsimMorris$X <- read.csv(\"{4}\")" + Environment.NewLine +
                                 "values = read.csv(\"{5}\")" + Environment.NewLine +
                                 "allEE <- data.frame()" + Environment.NewLine +
                                 "allStats <- data.frame()" + Environment.NewLine +
                                 "for (columnName in colnames(values))" + Environment.NewLine +
                                 "{{" + Environment.NewLine +
                                 "   apsimMorris$y <- values[[columnName]]" + Environment.NewLine +
                                 "   tell(apsimMorris)" + Environment.NewLine +

                                 "   ee <- data.frame(apsimMorris$ee)" + Environment.NewLine +
                                 "   ee$variable <- columnName" + Environment.NewLine +
                                 "   ee$path <- c(1:{1})" + Environment.NewLine +
                                 "   allEE <- rbind(allEE, ee)" + Environment.NewLine +

                                 "   mu <- apply(apsimMorris$ee, 2, mean)" + Environment.NewLine +
                                 "   mustar <- apply(apsimMorris$ee, 2, function(x) mean(abs(x)))" + Environment.NewLine +
                                 "   sigma <- apply(apsimMorris$ee, 2, sd)" + Environment.NewLine +
                                 "   stats <- data.frame(mu, mustar, sigma)" + Environment.NewLine +
                                 "   stats$param <- params" + Environment.NewLine +
                                 "   stats$variable <- columnName" + Environment.NewLine +
                                 "   allStats <- rbind(allStats, stats)" + Environment.NewLine +

                                 "}}" + Environment.NewLine +
                                 "write.csv(allEE,\"{6}\", row.names=FALSE)" + Environment.NewLine +
                                 "write.csv(allStats, \"{7}\", row.names=FALSE)" + Environment.NewLine,
                                    paramNames, NumPaths, lowerBounds, upperBounds,
                                    morrisParametersFileName.Replace("\\", "/"),
                                    apsimVariableFileName.Replace("\\", "/"),
                                    eeFileName.Replace("\\", "/"),
                                    statsFileName.Replace("\\", "/"));
                File.WriteAllText(rFileName, script);

                // Run R
                R r = new R();
                Console.WriteLine(r.GetPackage("sensitivity"));
                r.RunToTable(rFileName);


                // Get ee data from R and store in ee table.
                // EE data from R looks like:
                //         "ResidueWt",             "FASW",               "CN2",          "Cona",             "variable","path"
                //   - 22.971008269563,0.00950570342209862,-0.00379987333757356,56.7587080430652,"FallowEvaporation1996",1
                //   - 25.790599484188, 0.0170777988614538, -0.0265991133629069,58.0240658644712,"FallowEvaporation1996",2
                //   - 26.113599477728, 0.0113851992409871,  0.0113996200126667,57.9689677010766,"FallowEvaporation1996",3
                //   - 33.284199334316, 0.0323193916349732,  -0.334388853704853,60.5376820772641,"FallowEvaporation1996",4
                DataTable eeDataRaw = ApsimTextFile.ToTable(eeFileName);
                DataView eeView = new DataView(eeDataRaw);

                DataTable eeTable = new DataTable(Name + "ElementaryEffects");
                IndexedDataTable eeTableKey = new IndexedDataTable(eeTable, new string[] { "Param", "Year" });

                // Create a path variable.               
                var pathValues = Enumerable.Range(1, NumPaths).ToArray();

                foreach (var parameter in Parameters)
                {
                    foreach (DataColumn column in predictedValues.Columns)
                    {
                        eeView.RowFilter = "variable = '" + column.ColumnName + "'";
                        if (eeView.Count != NumPaths)
                            throw new Exception("Found only " + eeView.Count + " paths for variable " + column.ColumnName + " in ee table");
                        int year = Convert.ToInt32(column.ColumnName.Substring(column.ColumnName.Length - 4));
                        string variableName = column.ColumnName.Substring(0, column.ColumnName.Length - 4);

                        eeTableKey.SetIndex(new object[] { parameter.Name, year });

                        List<double> values = DataTableUtilities.GetColumnAsDoubles(eeView, parameter.Name).ToList();
                        var runningMean = MathUtilities.RunningAverage(values);

                        eeTableKey.SetValues("Path", pathValues);
                        eeTableKey.SetValues(variableName, runningMean);
                    }
                }

                // Get stats data from R and store in MuStar table.
                // Stats data coming back from R looks like:
                //                "mu",         "mustar",          "sigma",    "param","variable"
                //   -30.7331368183818, 30.7331368183818, 5.42917964248002,"ResidueWt","FallowEvaporation1996"
                // -0.0731299918470997,0.105740687296631,0.450848277601353,     "FASW","FallowEvaporation1996"
                //   -0.83061431285624,0.839772007599748, 1.75541097254145,      "CN2","FallowEvaporation1996"
                //    62.6942591520838, 62.6942591520838, 5.22778043503867,     "Cona","FallowEvaporation1996"
                //    -17.286285468283, 19.4018404625051, 24.1361388348929,"ResidueWt","FallowRunoff1996"
                //    8.09850688306722, 8.09852589447407, 15.1988107373113,     "FASW","FallowRunoff1996"
                //    18.6196168461051, 18.6196168461051, 15.1496277765849,      "CN2","FallowRunoff1996"
                //   -7.12794888887507, 7.12794888887507, 5.54014788597839,     "Cona","FallowRunoff1996"
                DataTable muStarTable = new DataTable(Name + "MuStar");
                IndexedDataTable tableKey = new IndexedDataTable(muStarTable, new string[2] { "Param", "Year" });

                DataTable statsDataRaw = ApsimTextFile.ToTable(statsFileName);
                foreach (DataRow row in statsDataRaw.Rows)
                {
                    string variable = row["variable"].ToString();
                    int year = Convert.ToInt32(variable.Substring(variable.Length - 4));
                    variable = variable.Substring(0, variable.Length - 4);
                    tableKey.SetIndex(new object[] { row["param"], year });

                    tableKey.Set(variable + ".mu", row["mu"]);
                    tableKey.Set(variable + ".mustar", row["mustar"]);
                    tableKey.Set(variable + ".sigma", row["sigma"]);

                    // Need to bring in the descriptive values.
                    view.RowFilter = "Clock.Today.Year=" + year;
                    foreach (var descriptiveColumnName in descriptiveColumnNames)
                    {
                        var values = DataTableUtilities.GetColumnAsStrings(view, descriptiveColumnName);
                        if (values.Distinct().Count() == 1)
                            tableKey.Set(descriptiveColumnName, view[0][descriptiveColumnName]);
                    }
                }
                
                dataStore.DeleteDataInTable(eeTable.TableName);
                dataStore.WriteTable(eeTable);
                dataStore.DeleteDataInTable(muStarTable.TableName);
                dataStore.WriteTable(muStarTable);
            }
        }

        /// <summary>Writes documentation for this function by adding to the list of documentation tags.</summary>
        /// <param name="tags">The list of tags to add to.</param>
        /// <param name="headingLevel">The level (e.g. H2) of the headings.</param>
        /// <param name="indent">The level of indentation 1, 2, 3 etc.</param>
        public void Document(List<AutoDocumentation.ITag> tags, int headingLevel, int indent)
        {
            if (IncludeInDocumentation)
            {
                // add a heading.
                tags.Add(new AutoDocumentation.Heading(Name, headingLevel));

                foreach (IModel child in Children)
                {
                    if (!(child is Simulation) && !(child is Factors))
                        AutoDocumentation.DocumentModel(child, tags, headingLevel + 1, indent);
                }
            }
        }

        /// <summary>A encapsulation of a parameter to analyse</summary>
        public class Parameter
        {
            /// <summary>Name of parameter</summary>
            public string Name;

            /// <summary>Model path of parameter</summary>
            public string Path;

            /// <summary>Lower bound of parameter</summary>
            public double LowerBound;

            /// <summary>Upper bound of parameter</summary>
            public double UpperBound;
        }


    }
}
