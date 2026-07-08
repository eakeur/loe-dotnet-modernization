namespace SampleApp.Shared;

// Fully qualifies its own project's namespace. Used to verify that a project does not
// get reported as a "usage" of its own root namespace (self-exclusion).
public class SelfReference
{
    public SampleApp.Shared.OrderDto Nested => new SampleApp.Shared.OrderDto();
}
