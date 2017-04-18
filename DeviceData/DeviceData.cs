﻿using HomeSeerAPI;
using NullGuard;
using Scheduler.Classes;
using System;
using System.Collections.Generic;
using System.IO;

namespace Hspi
{
    using static Hspi.StringUtil;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DeviceData : DeviceDataBase
    {
        protected DeviceData(string name, XmlPathData pathData, int deviceType = 0, [AllowNull]string initialStringValue = "--", double initialValue = 0D) :
            base(name, pathData)
        {
            this.deviceType = deviceType;
            this.initialStringValue = initialStringValue;
            this.initialValue = initialValue;
        }

        public abstract void UpdateDeviceData(IHSApplication HS, DeviceClass device, System.Xml.XPath.XPathNodeIterator value);

        public override int HSDeviceType => deviceType;
        public override string HSDeviceTypeString => INV($"{WUWeatherData.PlugInName} Information Device");
        public override string InitialString => initialStringValue;
        public override double InitialValue => initialValue;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Need for Filename")]
        protected static IList<VSVGPairs.VGPair> GetGraphicsPairsForEnum(Type enumType)
        {
            var pairs = new List<VSVGPairs.VGPair>();
            foreach (var value in Enum.GetValues(enumType))
            {
                var pair = new VSVGPairs.VGPair()
                {
                    PairType = VSVGPairs.VSVGPairType.SingleValue,
                    Graphic = Path.Combine(WUWeatherData.ImagesPathRoot, INV($"{value.ToString().ToLowerInvariant()}.png")),
                    Set_Value = (int)value,
                };
                pairs.Add(pair);
            }
            return pairs;
        }

        protected static IList<VSVGPairs.VSPair> GetStatusPairsForEnum(Type enumType)
        {
            var pairs = new List<VSVGPairs.VSPair>();
            foreach (var value in Enum.GetValues(enumType))
            {
                pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                {
                    PairType = VSVGPairs.VSVGPairType.SingleValue,
                    Value = (int)value,
                    Status = EnumHelper.GetDescription((System.Enum)value)
                });
            }

            return pairs;
        }

        private readonly int deviceType;
        private readonly double initialValue;
        private readonly string initialStringValue;
    };
}