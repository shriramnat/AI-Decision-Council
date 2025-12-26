# AI Decision Council

The **AI Decision Council** is a lightweight ASP.NET Core web application for orchestrating structured, multi-agent AI deliberation.

It enables multiple large language models hosted across Azure AI Foundry, OpenAI, xAI, and (soon) Google to independently analyze, critique, and refine a shared topic. By chaining model outputs and enforcing structured review cycles, the system delivers outcomes that are more balanced, robust, and defensible than single-model generation.

This readme covers a high-level overview and the minimal steps required to run the application locally.

## Core Concepts

- **Models** - External LLM endpoints (Azure, OpenAI, xAI) configured with credentials and model types.
- **Creator** - A primary agent responsible for generating and iterating on the draft based on reviewer feedback.
- **Reviewers** - Logical roles backed by models. Each agent uses a seed prompt to define its “personality” and evaluation lens.
- **Council** - A collection of reviewer agents that critique and validate a central draft.


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
Open the application. If running locally, go to http://localhost:5000. This is the home page where you will begin your experience

![Diagram](/images/mainpage-empty.png)

### Step 1: Configure Models
Before you create agents, you need to be able to configure the app with your models and provide the endpoint and API Key info. These details will be used when the model is invoked by the agent.
- Navigate to **Configure Models**
- Provide:
  - API Endpoint
  - API Key
  - Model Type
- Optionally export or import a `.modelsettings` file for reuse or versioning
> You can also add additional models to the app from this page. Models can be added from **OpenAI, xAI or those hosted in the Microsoft AI foundry Service**. Google and Anthropic implementations will be added later. (Feel free to contribute!)

![Diagram](/images/modeladditionwizard.png)

### Step 2: Configure the Agent Council
Now it's time to configure your reviewers. These are personas that will evaluate the generated content and provide actionable input to the Creator agent. By default the app comes configured with a default reviewer which you can customize to your liking.

![Diagram](/images/reviewers-base.png)

Additionally, the app features a gallery of reviewer templates that are designed to excel at specific tasks like data analytics, Code appropriateness, security controls etc. You can add these reviewer personas by clicking the Add reviewer button. 

![Diagram](/images/reviewertemplates.png)

You can also customize them by modifying the root prompt, once they are added.

> **Note**: In theory, you can add unlimited number of reviewers. However, beware that, just like in real life, more reviewers cause more churn towards converging the topic. So be mindful of adding more than 3-4 reviewers.

![Diagram](/images/mainpage-base.png)

### Step 3: Define a topic and Start a Session

- Define a clear, well-scoped topic
- Choose between:
  - **Start** (automatically go through iterations until convergence)
  - **Step-Once mode** (manual review between iterations)
- Start the session and observe deliberation in real time

![Diagram](/images/mainpage-filled.png)

## Additional Capabilities
- You can view the Live interaction stream between the agents by clicking the "View Agent Interactions" button. This will give you a pretty good idea of what each of the agents are sending out in each iteration.
![Diagram](/images/interactionstream.png)

- Session controls (pause, reset, download transcript) and Configurable session-level constraints and enforcement rules are accessible from the Seccion settings fly out. Click the gear icon next to the Stop button to access this.
![Diagram](/images/sessionsettings.png)

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
