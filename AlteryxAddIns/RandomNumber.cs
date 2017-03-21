﻿using OmniBus.Framework.EventHandlers;
using OmniBus.Framework.TypeConverters;

namespace JDunkerley.AlteryxAddIns
{
    using System;
    using System.ComponentModel;

    using AlteryxRecordInfoNet;

    using OmniBus.Framework;
    using OmniBus.Framework.Attributes;
    using OmniBus.Framework.ConfigWindows;
    using OmniBus.Framework.Factories;
    using OmniBus.Framework.Interfaces;

    public class RandomNumber :
        BaseTool<RandomNumber.Config, RandomNumber.Engine>, AlteryxGuiToolkit.Plugins.IPlugin
    {
        public enum Distribution
        {
            Uniform,
            Triangular,
            Normal,
            LogNormal
        }

        public class Config
        {
            static Config()
            {
                DefaultRandom = new Lazy<Random>(() => new Random());
            }

            private static Lazy<Random> DefaultRandom { get; }

            /// <summary>
            /// Gets or sets the type of the output.
            /// </summary>
            [Category("Output")]
            [Description("Alteryx Type for the Output Field")]
            [FieldList(FieldType.E_FT_Double, FieldType.E_FT_Float, FieldType.E_FT_Int16, FieldType.E_FT_Int32, FieldType.E_FT_Int64)]
            [TypeConverter(typeof(FixedListTypeConverter<FieldType>))]
            public FieldType OutputType { get; set; } = FieldType.E_FT_Double;

            /// <summary>
            /// Gets or sets the name of the output field.
            /// </summary>
            [Category("Output")]
            [Description("Field Name To Use For Output Field")]
            public string OutputFieldName { get; set; } = "Random";

            /// <summary>
            /// Gets or sets the initial seed.
            /// </summary>
            [Description("Seed for Random Number Generator (0 to use default)")]
            public int Seed { get; set; } = 0;

            /// <summary>
            /// Gets or sets the distribution.
            /// </summary>
            [Description("Distribution For Random Number")]
            public Distribution Distribution { get; set; } = Distribution.Uniform;

            /// <summary>
            /// Gets or sets the minimum boundary.
            /// </summary>
            [Description("Minimum Range Value (for bounded distributions)")]
            public double Minimum { get; set; } = 0;

            /// <summary>
            /// Gets or sets the minimum boundary.
            /// </summary>
            [Description("Maximum Range Value (for bounded distributions)")]
            public double Maximum { get; set; } = 1;

            /// <summary>
            /// Gets or sets the average.
            /// </summary>
            [Description("Average Used For Distributions. (Mean for Normal, Mu for LogNormal)")]
            public double Average { get; set; } = 0;

            /// <summary>
            /// Gets or sets the average.
            /// </summary>
            [Description("Standard Deviation Used For Distributions")]
            public double StandardDeviation { get; set; } = 1;

            /// <summary>
            /// ToString used for annotation
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                switch (this.Distribution)
                {
                    case Distribution.Uniform:
                        return $"{this.OutputFieldName}=Rand[{this.Minimum}, {this.Maximum}]";
                    case Distribution.Triangular:
                        return $"{this.OutputFieldName}=Tri[{this.Minimum}, {this.Average}, {this.Maximum}]";
                    case Distribution.Normal:
                    case Distribution.LogNormal:
                        return $"{this.OutputFieldName}={this.Distribution}[{this.Average}, {this.StandardDeviation}]";
                }
                return "";
            }

            public Func<double> CreateRandomFunc()
            {
                var random = this.Seed == 0 ? DefaultRandom.Value : new Random(this.Seed);

                switch (this.Distribution)
                {
                    case Distribution.Triangular:
                        return () => this.TriangularNumber(random.NextDouble());
                    case Distribution.Normal:
                        {
                            var normal = new MathNet.Numerics.Distributions.Normal(
                                this.Average,
                                this.StandardDeviation,
                                random);
                            return () => normal.Sample();
                        }
                    case Distribution.LogNormal:
                        {
                            var logNormal = new MathNet.Numerics.Distributions.LogNormal(
                                this.Average,
                                this.StandardDeviation,
                                random);
                            return () => logNormal.Sample();
                        }
                    case Distribution.Uniform:
                        return () => random.NextDouble() * (this.Maximum - this.Minimum) + this.Minimum;
                }

                return () => double.NaN;
            }

            private double TriangularNumber(double p)
            {
                return p < (this.Average - this.Minimum) / (this.Maximum - this.Minimum)
                           ? this.Minimum + Math.Sqrt(p * (this.Maximum - this.Minimum) * (this.Average - this.Minimum))
                           : this.Maximum - Math.Sqrt((1 - p) * (this.Maximum - this.Minimum) * (this.Maximum - this.Average));
            }
        }

        public class Engine : BaseEngine<Config>
        {
            private Func<double> _nextValue;

            private FieldBase _outputFieldBase;

            /// <summary>
            /// Constructor For Alteryx
            /// </summary>
            public Engine()
                : this(new RecordCopierFactory(), new InputPropertyFactory(), new OutputHelperFactory())
            {
            }

            /// <summary>
            /// Create An Engine for unit testing.
            /// </summary>
            /// <param name="recordCopierFactory">Factory to create copiers</param>
            /// <param name="inputPropertyFactory">Factory to create input properties</param>
            /// <param name="outputHelperFactory">Factory to create output helpers</param>
            internal Engine(IRecordCopierFactory recordCopierFactory, IInputPropertyFactory inputPropertyFactory, IOutputHelperFactory outputHelperFactory)
                : base(recordCopierFactory, outputHelperFactory)
            {
                this.Input = inputPropertyFactory.Build(recordCopierFactory, this.ShowDebugMessages);
                this.Input.InitCalled += this.OnInit;
                this.Input.ProgressUpdated += (sender, args) => this.Output?.UpdateProgress(args.Progress, true);
                this.Input.RecordPushed += this.OnRecordPushed;
                this.Input.Closed += sender => this.Output?.Close(true);
            }

            /// <summary>
            /// Gets the input stream.
            /// </summary>
            [CharLabel('I')]
            public IInputProperty Input { get; }

            /// <summary>
            /// Gets or sets the output stream.
            /// </summary>
            [CharLabel('O')]
            public IOutputHelper Output { get; set; }

            private void OnInit(IInputProperty sender, SuccessEventArgs args)
            {
                this._nextValue = this.ConfigObject.CreateRandomFunc();

                var fieldDescription = new FieldDescription(
                                           this.ConfigObject.OutputFieldName,
                                           this.ConfigObject.OutputType)
                                           {
                                               Source = nameof(RandomNumber),
                                               Description =
                                                   $"Random Number {this.ConfigObject.ToString().Replace($"{this.ConfigObject.OutputFieldName}=", "")}"
                                           };

                this.Output?.Init(FieldDescription.CreateRecordInfo(this.Input.RecordInfo, fieldDescription));
                this._outputFieldBase = this.Output?[this.ConfigObject.OutputFieldName];

                args.Success = true;
            }

            private void OnRecordPushed(IInputProperty sender, RecordPushedEventArgs args)
            {
                var record = this.Output.Record;
                record.Reset();

                this.Input.Copier.Copy(record, args.RecordData);

                double val = this._nextValue();
                this._outputFieldBase.SetFromDouble(record, val);

                this.Output.Push(record);
                args.Success = true;
            }
        }
    }
}