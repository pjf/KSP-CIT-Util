using System.Collections.Generic;
using System.Linq;

namespace CIT_Util.Converter
{
    public static class Extensions
    {
        private static IEnumerable<Part> FindAllFuelLineConnectedSourceParts(this Part refPart, List<Part> allParts, bool outRes)
        {
            return allParts.OfType<FuelLine>()
                           .Where(fl => fl.target != null && fl.parent != null && outRes ? fl.parent == refPart : fl.target == refPart)
                           .Select(fl => outRes ? fl.target : fl.parent);
        }

        public static List<Part> FindPartsInSameResStack(this Part refPart, List<Part> allParts, HashSet<Part> searchedParts, bool outRes)
        {
            var partList = new List<Part> {refPart};
            searchedParts.Add(refPart);
            foreach (var attachNode in refPart.attachNodes.Where(an => an.attachedPart != null && !searchedParts.Contains(an.attachedPart) && an.attachedPart.fuelCrossFeed && an.nodeType == AttachNode.NodeType.Stack))
            {
                partList.AddRange(attachNode.attachedPart.FindPartsInSameResStack(allParts, searchedParts, outRes));
            }
            foreach (var fuelLinePart in refPart.FindAllFuelLineConnectedSourceParts(allParts, outRes).Where(flp => !searchedParts.Contains(flp)))
            {
                partList.AddRange(fuelLinePart.FindPartsInSameResStack(allParts, searchedParts, outRes));
            }
            return partList;
        }

        public static List<Part> FindPartsInSameStage(this Part refPart, List<Part> allParts, bool outRes)
        {
            var partList = allParts.Where(vPart => vPart.inverseStage == refPart.inverseStage).ToList();
            partList.AddRange(refPart.FindAllFuelLineConnectedSourceParts(allParts, outRes));
            return partList;
        }
    }
}