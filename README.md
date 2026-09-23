# com.arisen.samples

Capability gallery for Arisen Engine.

This package is the single place where an engine capability can be seen running. It owns the sample
catalogue, the hub entry scene, and the launch target each sample is reached through. It holds no
engine behaviour of its own: a sample composes packages that already exist, and this package decides
which of them are selected and which scene and world they start in.

## Why it exists

A profile decides the package graph at boot, so capability has no catalogue today: a capability can
only be seen by knowing which workspace, profile, and solution to launch. The gallery gives every
capability one entry point, one expected visual, and one bounded gate.

## Rules

- **A sample is data, not code.** Adding one adds a catalogue entry and its content, not a new build.
- **Every sample boots directly through `--sample <id>`.** The hub selects between samples; it is
  never the only way in, and no automated gate drives the hub UI.
- **A sample declares the packages it needs.** Its dependency closure must not contain a package it
  did not declare, so a sample cannot pass only because a neighbour happened to satisfy a
  dependency.
- **The hub is stateless.** No sample may depend on state another sample left behind.
- **The first deliverable of an engine track is its sample**, and that sample must be gated.

## Sample classes

A sample declares what it needs from the package graph, because the graph is mounted once at boot:

- **Content**: the same package set, a different scene or world. Selected inside a running process.
- **Capability**: a different package set or a different build switch. Reached through its own
  profile and a process restart.

## Status

Package skeleton only. The catalogue asset, the hub scene, the `--sample <id>` launch target, and the
per-sample gates are not implemented yet. The plan and the acceptance rules live in the engine
repository at `Arisen\Docs\Architecture\ArisenDevelopmentRoadmap.md`.
