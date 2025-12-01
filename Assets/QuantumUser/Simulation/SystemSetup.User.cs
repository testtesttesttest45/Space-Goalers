namespace Quantum {
  using System;
  using System.Collections.Generic;

  public static partial class DeterministicSystemSetup {
    static partial void AddSystemsUser(ICollection<SystemBase> systems, RuntimeConfig gameConfig, SimulationConfig simulationConfig, SystemsConfig systemsConfig) {
      // The system collection is already filled with systems comging from the SystemsConfig.       // Add or remove systems to the collection: systems.Add(new SystemFoo());
    }
  }
}