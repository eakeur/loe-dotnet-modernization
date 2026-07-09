namespace TextSearchSample.App;

// Deliberately avoids referencing the scan target package (even as plain text, e.g. in a
// comment) in any form -- exercises the true-negative case for both scanning passes.
public class Unrelated
{
    public int Add(int a, int b) => a + b;
}
