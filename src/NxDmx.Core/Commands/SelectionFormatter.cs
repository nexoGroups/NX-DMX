namespace NxDmx.Core.Commands;

public static class SelectionFormatter
{
    /// <summary>Writes numbers the way the operator would type them: <c>1 Thru 5 + 8</c>.</summary>
    public static string Format(IEnumerable<int> ids)
    {
        var sorted = ids.Distinct().Order().ToList();
        var parts = new List<string>();

        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1)
                j++;

            if (j - i >= 2)
            {
                parts.Add($"{sorted[i]} Thru {sorted[j]}");
            }
            else
            {
                for (var k = i; k <= j; k++)
                    parts.Add(sorted[k].ToString());
            }

            i = j + 1;
        }

        return string.Join(" + ", parts);
    }
}
