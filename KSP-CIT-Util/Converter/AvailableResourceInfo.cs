using System;

namespace CIT_Util.Converter
{
    internal class AvailableResourceInfo
    {
        internal bool AllowOverflow { get; set; }
        internal double AvailableAmount { get; set; }
        internal double AvailableSpace { get; set; }
        internal bool OutputResource { get; set; }
        internal double PercentageFilled { get; set; }
        internal int ResourceId { get; set; }

        internal AvailableResourceInfo(int id, bool outres, double available, double availSpace, bool allowOverflow = false)
        {
            this.ResourceId = id;
            this.OutputResource = outres;
            this.AvailableAmount = available;
            this.AvailableSpace = availSpace;
            this.PercentageFilled = this.AvailableAmount/this.AvailableSpace;
            this.AllowOverflow = allowOverflow;
        }

        internal bool CanTakeDemand(double demand)
        {
            if (this.OutputResource)
            {
                if (this.AllowOverflow)
                {
                    return true;
                }
                return Math.Abs(this.AvailableSpace - demand) > ConvUtil.Epsilon;
            }
            return Math.Abs(this.AvailableAmount - demand) > ConvUtil.Epsilon;
        }

        internal int TimesCanTakeDemand(double demand)
        {
            double times;
            if (this.OutputResource)
            {
                if (this.AllowOverflow)
                {
                    return int.MaxValue;
                }
                times = this.AvailableSpace/demand;
            }
            else
            {
                times = this.AvailableAmount/demand;
            }
            return (int) Math.Floor(times);
        }
    }
}