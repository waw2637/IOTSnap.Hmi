# IOTSnap.Hmi

Local-first HMI scaffold for an edge-hosted operator interface.

## Intended Shape

- Blazor Web App with interactive server rendering
- local cookie authentication
- SQLite-backed configuration and user storage
- local OPC UA runtime layer
- simple touch-friendly HMI shell inspired by the Dashboard visual language

## Solution Layout

- `src/IOTSnap.Hmi` - web host, pages, auth endpoints, shell UI
- `src/IOTSnap.Hmi.Data` - EF Core models and DbContext
- `src/IOTSnap.Hmi.Runtime` - OPC UA runtime abstractions and hosted services

## Current State

This repo is an intentional scaffold, not a finished HMI.

Included now:

- local auth wiring with first-run setup workflow
- SQLite configuration and starter entities
- OPC UA runtime service with initial client/session/subscription scaffolding
- starter pages for home, settings, comms, and user management
- first EF Core migration scaffolding with startup migration execution
- Dashboard-inspired shell styling

Still to do next:

- complete the login UX polish
- build the tag browser and comms editor
- add alarm/trend/operator screens

## First Run Setup

On first launch, if no local users exist, the app redirects to `/setup`.
Use that screen to create the first admin account and optional initial OPC UA endpoint profile.

