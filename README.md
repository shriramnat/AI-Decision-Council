# AI Decision Council

The **AI Decision Council** is a lightweight ASP.NET Core web application for orchestrating structured, multi-agent AI deliberation.

It enables multiple large language models hosted across Azure AI Foundry, OpenAI, xAI, and (soon) Google to independently analyze, critique, and refine a shared topic. By chaining model outputs and enforcing structured review cycles, the system delivers outcomes that are more balanced, robust, and defensible than single-model generation.

This readme covers a high-level overview and the minimal steps required to run the application locally.

## Core Concepts

- **Models**  
  External LLM endpoints (Azure, OpenAI, xAI) configured with credentials and model types.
- **Creator**  
  A primary agent responsible for generating and iterating on the draft based on reviewer feedback.
- **Reviewers**  
  Logical roles backed by models. Each agent uses a seed prompt to define its “personality” and evaluation lens.

- **Council**  
  A collection of reviewer agents that critique and validate a central draft.


## How the App Works

1. **Draft Creation**  
   The Creator agent generates an initial draft from the provided topic.

2. **Parallel Review**  
   The draft is sent to all configured reviewer agents for critique.

3. **Iterative Refinement**  
   Reviewer feedback is consolidated and fed back to the Creator. This loop continues until all reviewers sign off.

4. **Final Output**  
   Once consensus is reached, the final version is produced.



## Using the Application
Open the application. If running locally, go to http://localhost:5000

![Diagram](/images/mainpage-empty.png)

### Step 1: Configure Models

- Navigate to **Configure Models**
- Provide:
  - API Endpoint
  - API Key
  - Model Type
- Optionally export or import a `.modelsettings` file for reuse or versioning
> You can also add additional models to the app from this page. Models can be added from **OpenAI, xAI or those hosted in the Microsoft AI foundry Service**. Google and Anthropic implementations will be added later. (Feel free to contribute!)

![Diagram](/images/modeladditionwizard.png)

### Step 2: Configure the Agent Council

- Create one or more reviewer agents
- Assign seed prompts to define each agent’s perspective
- Agents can be tailored for:
  - Technical review
  - Decision analysis
  - Content quality
  - Risk evaluation

  > Pre-configured agent templates are planned for future releases.

![Diagram](/images/mainpage-base.png)

![Diagram](/images/reviewertemplates.png)

### Step 3: Start a Session

- Define a clear, well-scoped topic
- Choose:
  - **Continuous mode** (automatic iteration)
  - **Step-wise mode** (manual review between iterations)
- Start the session and observe deliberation in real time

![Diagram](/images/mainpage-filled.png)

## Additional Capabilities

- Session controls (pause, reset, download transcript). Configurable session-level constraints and enforcement rules
![Diagram](/images/sessionsettings.png)

- Live interaction stream via SignalR
![Diagram](/images/interactionstream.png)

## Running the App Locally

### Prerequisites

.NET 8 SDK  
  ```powershell
  dotnet --version
```
Optional: Visual Studio, VS Code, or another IDE

### Clone the Repository
  ```powershell
  git clone https://github.com/shriramnat/AI-Decision-Council.git
cd AI-Decision-Council
```
### Build the Application
  ```powershell
  dotnet restore
  dotnet build DXO/DXO.csproj -c Debug
```
Alternatively, open DXO.sln in Visual Studio and build the solution.

### Run the Application
  ```powershell
  dotnet run --project DXO/DXO.csproj
```
> **Notes:**
>- Uses appsettings.Development.json when running in the Development environment
>- Serves Razor Pages and exposes a SignalR hub for real-time client updates

### Configuration Notes
Server URLs and ports can be configured via:
- DXO/Properties/launchSettings.json
- The ASPNETCORE_URLS environment variable

### Security Features

- API keys stored server-side only (never sent to the browser)
- Configurable per-IP rate limiting
- Security headers (CSP, X-Frame-Options, etc.)
- Input validation and length limits
- HTTPS enforcement in production

## Troubleshooting
**Configuration Load Errors**
- Ensure the application is running and accessible
- Verify required configuration values are present

**External Model API Errors**
- Confirm API keys are correctly configured
- Ensure sufficient quota is available
- Verify network access to external model providers

**SignalR Connection Issues**
- Check the browser console for WebSocket errors
- Verify that proxies or firewalls are not blocking WebSocket traffic

## Contributing & License

- Contributions are welcome via pull requests
- See the [LICENSE](LICENSE) file for license terms.
