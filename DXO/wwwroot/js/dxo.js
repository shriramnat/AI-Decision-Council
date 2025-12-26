// DXO - Creator-Reviewer Orchestration JavaScript

class DXOApp {
    constructor() {
        this.connection = null;
        this.currentSessionId = null;
        this.config = null;
        this.messages = [];
        this.isRunning = false;
        this.currentStreamingMessage = null;
        this.reviewers = []; // Dynamic reviewers array
        this.reviewerCounter = 0; // Counter for generating unique IDs
        
        this.init();
    }

    async init() {
        await this.loadConfig();
        this.setupSignalR();
        this.setupEventListeners();
        await this.loadDefaultPrompts(); // Load prompts from agentconfigurations.json
        this.createToastContainer();
        this.addDefaultReviewer(); // Add one default reviewer

        // Setup scroll buttons (floating top/bottom controls)
        this.setupScrollButtons();
    }

    // Load configuration from server
    async loadConfig() {
        try {
            const response = await fetch('/api/config');
            this.config = await response.json();
            this.populateModelDropdowns();
        } catch (error) {
            console.error('Failed to load config:', error);
            this.showToast('Failed to load configuration', 'error');
        }
    }

    // Populate model dropdowns with available models
    populateModelDropdowns() {
        const creatorSelect = document.getElementById('creatorModel');
        
        if (!this.config?.allowedModels) return;

        // Clear existing options first to prevent duplicates
        creatorSelect.innerHTML = '';

        // Populate creator model dropdown
        this.config.allowedModels.forEach(model => {
            const option = new Option(model, model);
            creatorSelect.add(option);
        });

        creatorSelect.value = this.config.defaultModelCreator;
        
        // Add change event listener to handle xAI model selection
        creatorSelect.addEventListener('change', () => this.handleCreatorModelChange());
        
        // Initial state check
        this.handleCreatorModelChange();
    }

    // Populate a reviewer model dropdown
    populateReviewerModelDropdown(selectElement) {
        if (!this.config?.allowedModels) return;

        // Clear existing options first to prevent duplicates
        selectElement.innerHTML = '';

        this.config.allowedModels.forEach(model => {
            const option = new Option(model, model);
            selectElement.add(option);
        });

        selectElement.value = this.config.defaultModelReviewer;
    }

    // Generate default reviewer prompt
    getDefaultReviewerPrompt(reviewerNumber) {
        return `You are DXO Reviewer ${reviewerNumber}.

Review rubric (must address each area):
A) Technical correctness: identify incorrect claims, missing assumptions, and logic gaps.
B) Completeness: ensure all required sections exist and content is sufficiently detailed.
C) Clarity & structure: enforce clear flow, strong narrative, and unambiguous definitions.
D) Audience fit: ensure content matches the target audience's expertise level.

Output format:
1) Summary verdict (1 paragraph)
2) Major issues (bulleted, each with rationale + concrete fix)
3) Minor issues (bulleted)
4) Checklist (pass/fail items)

If and only if the draft is publication-ready from your perspective, include the token @@SIGNED OFF@@ on its own line at the end.`;
    }

    // Add a default reviewer on init - loads from agentconfigurations.json
    async addDefaultReviewer() {
        try {
            const response = await fetch('/agentconfigurations.json');
            if (!response.ok) {
                throw new Error('Failed to load agent configurations');
            }
            
            const config = await response.json();
            const customReviewer = config.agents?.reviewers?.find(r => r.agentId === 'CustomReviewer');
            
            if (customReviewer && customReviewer.prompt) {
                // Format the prompt by replacing \n with actual line breaks
                const formattedPrompt = customReviewer.prompt.replace(/\\n/g, '\n');
                this.addReviewer(customReviewer.role, formattedPrompt);
            } else {
                // Fallback to default prompt if Custom Reviewer not found
                console.warn('Custom Reviewer not found in agentconfigurations.json, using fallback');
                this.addReviewer('Reviewer 1', this.getDefaultReviewerPrompt(1));
            }
        } catch (error) {
            console.error('Failed to load default reviewer:', error);
            // Use fallback on error
            this.addReviewer('Reviewer 1', this.getDefaultReviewerPrompt(1));
        }
    }

    // Add a new reviewer
    addReviewer(name = null, prompt = null) {
        this.reviewerCounter++;
        const reviewerId = `reviewer-${Date.now()}-${this.reviewerCounter}`;
        const reviewerNumber = this.reviewers.length + 1;
        const reviewerName = name || `Reviewer ${reviewerNumber}`;
        const reviewerPrompt = prompt || this.getDefaultReviewerPrompt(reviewerNumber);

        // Create reviewer object
        const reviewer = {
            id: reviewerId,
            name: reviewerName,
            prompt: reviewerPrompt
        };
        this.reviewers.push(reviewer);

        // Clone template and populate
        const template = document.getElementById('reviewerCardTemplate');
        const clone = template.content.cloneNode(true);
        const card = clone.querySelector('.persona-card');
        
        card.dataset.reviewerId = reviewerId;
        card.querySelector('.reviewer-name-input').value = reviewerName;
        card.querySelector('.reviewer-prompt').value = reviewerPrompt;

        // Populate model dropdown
        const modelSelect = card.querySelector('.reviewer-model');
        this.populateReviewerModelDropdown(modelSelect);

        // Add to container
        const container = document.getElementById('reviewerCardsContainer');
        container.appendChild(clone);

        // Setup event listeners for this card
        this.setupReviewerCardListeners(reviewerId);

        // Apply dynamic color based on reviewer index
        this.updateReviewerColors();

        return reviewerId;
    }

    // Setup event listeners for a reviewer card
    setupReviewerCardListeners(reviewerId) {
        const card = document.querySelector(`[data-reviewer-id="${reviewerId}"]`);
        if (!card) return;

        // Delete button
        const deleteBtn = card.querySelector('.btn-delete-reviewer');
        deleteBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            this.deleteReviewer(reviewerId);
        });

        // Reset memory button
        const resetBtn = card.querySelector('.btn-reset-reviewer');
        resetBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            this.resetReviewerMemory(reviewerId);
        });

        // Name input change
        const nameInput = card.querySelector('.reviewer-name-input');
        nameInput.addEventListener('change', (e) => {
            this.updateReviewerName(reviewerId, e.target.value);
        });

        // Model dropdown change
        const modelSelect = card.querySelector('.reviewer-model');
        modelSelect.addEventListener('change', () => {
            this.handleReviewerModelChange(reviewerId);
        });

        // Initial state check for penalty fields
        this.handleReviewerModelChange(reviewerId);
    }

    // Delete a reviewer
    deleteReviewer(reviewerId) {
        // Don't allow deleting if only one reviewer remains
        if (this.reviewers.length <= 1) {
            this.showToast('At least one reviewer is required', 'warning');
            return;
        }

        // Remove from array
        this.reviewers = this.reviewers.filter(r => r.id !== reviewerId);

        // Remove from DOM
        const card = document.querySelector(`[data-reviewer-id="${reviewerId}"]`);
        if (card) {
            card.remove();
        }

        // Update colors
        this.updateReviewerColors();

        this.showToast('Reviewer removed', 'info');
    }

    // Update reviewer name
    updateReviewerName(reviewerId, newName) {
        const reviewer = this.reviewers.find(r => r.id === reviewerId);
        if (reviewer) {
            reviewer.name = newName;
        }
    }

    // Reset reviewer memory
    async resetReviewerMemory(reviewerId) {
        if (!this.currentSessionId) {
            this.showToast('No active session', 'warning');
            return;
        }

        // For now, we'll use a generic approach
        // The backend will need to be updated to handle reviewer IDs
        this.showToast(`Memory reset requested for reviewer`, 'info');
    }

    // Update reviewer card colors based on their index
    updateReviewerColors() {
        const colors = [
            { border: 'rgba(100, 210, 255, 0.3)', text: '#64d2ff' },
            { border: 'rgba(255, 159, 10, 0.3)', text: '#ff9f0a' },
            { border: 'rgba(48, 209, 88, 0.3)', text: '#30d158' },
            { border: 'rgba(255, 69, 58, 0.3)', text: '#ff453a' },
            { border: 'rgba(191, 90, 242, 0.3)', text: '#bf5af2' }
        ];

        const cards = document.querySelectorAll('#reviewerCardsContainer .persona-card');
        cards.forEach((card, index) => {
            const colorSet = colors[index % colors.length];
            card.style.borderColor = colorSet.border;
            const header = card.querySelector('.card-header h3, .card-header .reviewer-name-container');
            if (header) {
                header.style.color = colorSet.text;
            }
        });
    }

    // Load default prompts from agentconfigurations.json
    async loadDefaultPrompts() {
        try {
            const response = await fetch('/agentconfigurations.json');
            if (!response.ok) {
                throw new Error('Failed to load agent configurations');
            }
            
            const config = await response.json();
            const creatorConfig = config.agents?.creator;
            
            if (creatorConfig && creatorConfig.prompt) {
                // Format the prompt by replacing \n with actual line breaks
                const formattedPrompt = creatorConfig.prompt.replace(/\\n/g, '\n');
                document.getElementById('creatorPrompt').value = formattedPrompt;
            } else {
                // Fallback to default prompt if not found in JSON
                console.warn('Creator prompt not found in agentconfigurations.json, using fallback');
                const fallbackPrompt = `You are DXO Creator, an expert technical author. Your job is to write high-quality technical content for a technical audience.

Authoring rules:
1) Structure the content with clear sections appropriate to the topic.
2) Use precise technical language and define terms on first use.
3) Do not invent facts, benchmarks, citations, or references.
4) Include diagrams/tables as placeholders when helpful.
5) Maintain internal consistency across sections.
6) Incorporate feedback from ALL reviewers explicitly.
7) When complete (after ALL reviewers approve), output: FINAL: followed by the final content.`;
                document.getElementById('creatorPrompt').value = fallbackPrompt;
            }
        } catch (error) {
            console.error('Failed to load default prompts:', error);
            this.showToast('Failed to load Creator prompt from configuration', 'warning');
        }
    }

    // Setup SignalR connection
    setupSignalR() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/dxo')
            .withAutomaticReconnect()
            .build();

        // Session events
        this.connection.on('SessionStarted', (sessionId) => {
            this.updateStatus('Running');
            this.showToast('Session started', 'success');
        });

        this.connection.on('SessionPaused', (sessionId) => {
            this.updateStatus('Paused');
            this.showToast('Session paused', 'info');
        });

        this.connection.on('SessionStopped', (sessionId, reason) => {
            this.updateStatus('Stopped');
            this.isRunning = false;
            this.updateButtonStates();
            this.showToast(`Session stopped: ${reason}`, 'warning');
        });

        this.connection.on('SessionCompleted', (sessionId, finalContent, stopReason) => {
            this.updateStatus('Completed');
            this.isRunning = false;
            this.updateButtonStates();
            document.getElementById('finalOutput').value = finalContent;
            this.showToast(`Session completed: ${stopReason}`, 'success');
        });

        this.connection.on('SessionError', (sessionId, error) => {
            this.updateStatus('Error');
            this.isRunning = false;
            this.updateButtonStates();
            this.showToast(`Error: ${error}`, 'error');
        });

        // Iteration events
        this.connection.on('IterationStarted', (sessionId, iteration) => {
            this.addIterationHeader(iteration);
            this.updateIterationCount(iteration);
        });

        // Message events
        this.connection.on('MessageStarted', (sessionId, messageId, persona, iteration) => {
            this.startStreamingMessage(messageId, persona, iteration);
        });

        this.connection.on('MessageChunk', (sessionId, messageId, content) => {
            this.appendToStreamingMessage(messageId, content);
        });

        this.connection.on('MessageCompleted', (sessionId, messageId, fullContent) => {
            this.completeStreamingMessage(messageId, fullContent);
        });

        // Memory events
        this.connection.on('PersonaMemoryReset', (sessionId, persona) => {
            this.showToast(`${persona} memory reset`, 'info');
        });

        // Start connection
        this.connection.start()
            .then(() => console.log('SignalR connected'))
            .catch(err => {
                console.error('SignalR connection error:', err);
                this.showToast('Failed to connect to server', 'error');
            });
    }

    // Setup event listeners
    setupEventListeners() {
        // Session Settings gear button
        document.getElementById('btnSessionSettings').addEventListener('click', () => this.openSessionSettings());
        
        // View Interactions button
        document.getElementById('btnViewInteractions').addEventListener('click', () => this.openInteractionStream());
        
        // Action buttons
        document.getElementById('btnStart').addEventListener('click', () => this.startSession());
        document.getElementById('btnStep').addEventListener('click', () => this.stepSession());
        document.getElementById('btnStop').addEventListener('click', () => this.stopSession());
        document.getElementById('btnReset').addEventListener('click', () => this.resetSession());
        document.getElementById('btnExport').addEventListener('click', () => this.showExportModal());

        // Memory reset buttons
        document.getElementById('btnResetCreator').addEventListener('click', () => this.resetPersonaMemory('Creator'));

        // Add Reviewer button - open selector flyout
        document.getElementById('btnAddReviewer').addEventListener('click', () => {
            this.openReviewerSelector();
        });

        // Output buttons
        document.getElementById('btnCopyOutput').addEventListener('click', () => this.copyOutput());
        document.getElementById('btnDownloadMd').addEventListener('click', () => this.downloadOutput('md'));
        document.getElementById('btnDownloadTxt').addEventListener('click', () => this.downloadOutput('txt'));

        // Export modal buttons
        document.getElementById('btnExportJson').addEventListener('click', () => this.exportTranscript('json'));
        document.getElementById('btnExportMd').addEventListener('click', () => this.exportTranscript('md'));

        // Clear trace button
        document.getElementById('btnClearTrace').addEventListener('click', () => this.clearTrace());

        // Max iterations change
        document.getElementById('maxIterations').addEventListener('change', (e) => {
            this.updateIterationCount(0);
        });
    }

    // --- Scroll FAB (Top / Bottom) handling ---
    setupScrollButtons() {
        // find or create buttons (Layout includes them, but guard if not)
        this.scrollFab = document.getElementById('scrollFabContainer');
        this.scrollTopBtn = document.getElementById('scrollTopBtn');
        this.scrollBottomBtn = document.getElementById('scrollBottomBtn');

        if (!this.scrollFab) {
            // create container if missing
            this.scrollFab = document.createElement('div');
            this.scrollFab.id = 'scrollFabContainer';
            this.scrollFab.className = 'scroll-fab';
            document.body.appendChild(this.scrollFab);
        }

        if (!this.scrollTopBtn) {
            this.scrollTopBtn = document.createElement('button');
            this.scrollTopBtn.id = 'scrollTopBtn';
            this.scrollTopBtn.className = 'scroll-fab-btn';
            this.scrollTopBtn.title = 'Scroll to Top';
            this.scrollTopBtn.innerText = '↑';
            this.scrollFab.appendChild(this.scrollTopBtn);
        }

        if (!this.scrollBottomBtn) {
            this.scrollBottomBtn = document.createElement('button');
            this.scrollBottomBtn.id = 'scrollBottomBtn';
            this.scrollBottomBtn.className = 'scroll-fab-btn secondary';
            this.scrollBottomBtn.title = 'Scroll to Bottom';
            this.scrollBottomBtn.innerText = '↓';
            this.scrollFab.appendChild(this.scrollBottomBtn);
        }

        // Click handlers
        this.scrollTopBtn.addEventListener('click', (e) => {
            e.preventDefault();
            window.scrollTo({ top: 0, behavior: 'smooth' });
        });

        this.scrollBottomBtn.addEventListener('click', (e) => {
            e.preventDefault();
            const bottom = document.documentElement.scrollHeight - window.innerHeight;
            window.scrollTo({ top: bottom, behavior: 'smooth' });
        });

        // Update visibility on scroll/resize and on load
        this._scrollFabUpdateTimer = null;
        const update = () => this.updateScrollFabVisibility();
        window.addEventListener('scroll', () => {
            // throttle updates
            if (this._scrollFabUpdateTimer) clearTimeout(this._scrollFabUpdateTimer);
            this._scrollFabUpdateTimer = setTimeout(update, 50);
        }, { passive: true });
        window.addEventListener('resize', update);
        // initial state
        update();
    }

    updateScrollFabVisibility() {
        const threshold = 20;
        const scrollY = window.scrollY || document.documentElement.scrollTop || 0;
        const atTop = scrollY <= threshold;
        const atBottom = (window.innerHeight + scrollY) >= (document.documentElement.scrollHeight - threshold);

        // If in middle: show both side-by-side
        if (!atTop && !atBottom) {
            this.showElement(this.scrollTopBtn);
            this.showElement(this.scrollBottomBtn);
            return;
        }

        // If at top -> show only "Scroll to Bottom" (per requirement clicking to top should turn into bottom)
        if (atTop) {
            this.hideElement(this.scrollTopBtn);
            this.showElement(this.scrollBottomBtn);
            return;
        }

        // If at bottom -> show only "Scroll to Top"
        if (atBottom) {
            this.showElement(this.scrollTopBtn);
            this.hideElement(this.scrollBottomBtn);
            return;
        }
    }

    showElement(el) {
        if (!el) return;
        el.classList.remove('scroll-fab-hidden');
    }

    hideElement(el) {
        if (!el) return;
        el.classList.add('scroll-fab-hidden');
    }

    // Get all reviewer configurations from the UI
    getReviewerConfigs() {
        const configs = [];
        const cards = document.querySelectorAll('#reviewerCardsContainer .persona-card');
        
        cards.forEach(card => {
            const reviewerId = card.dataset.reviewerId;
            const reviewer = this.reviewers.find(r => r.id === reviewerId);
            
            configs.push({
                id: reviewerId,
                name: card.querySelector('.reviewer-name-input').value,
                rootPrompt: card.querySelector('.reviewer-prompt').value,
                model: card.querySelector('.reviewer-model').value,
                temperature: parseFloat(card.querySelector('.reviewer-temp').value),
                maxTokens: parseInt(card.querySelector('.reviewer-max-tokens').value),
                topP: parseFloat(card.querySelector('.reviewer-top-p').value),
                presencePenalty: parseFloat(card.querySelector('.reviewer-presence-penalty').value),
                frequencyPenalty: parseFloat(card.querySelector('.reviewer-frequency-penalty').value)
            });
        });
        
        return configs;
    }

    // Create session and start
    async startSession() {
        const request = this.buildSessionRequest();
        
        try {
            // Create session
            const response = await fetch('/api/session/create', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request)
            });

            if (!response.ok) {
                throw new Error('Failed to create session');
            }

            const session = await response.json();
            this.currentSessionId = session.sessionId;
            document.getElementById('sessionId').textContent = session.sessionId.substring(0, 8) + '...';

            // Join SignalR group
            await this.connection.invoke('JoinSession', this.currentSessionId);

            // Start session
            const startResponse = await fetch(`/api/session/${this.currentSessionId}/start`, {
                method: 'POST'
            });

            if (!startResponse.ok) {
                const error = await startResponse.json();
                throw new Error(error.error || 'Failed to start session');
            }

            this.isRunning = true;
            this.updateButtonStates();
            this.clearTrace();

        } catch (error) {
            console.error('Failed to start session:', error);
            this.showToast(error.message, 'error');
        }
    }

    // Step through one iteration
    async stepSession() {
        if (!this.currentSessionId) {
            // Need to create session first
            await this.createSessionForStep();
        }

        try {
            const response = await fetch(`/api/session/${this.currentSessionId}/step`, {
                method: 'POST'
            });

            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.error || 'Failed to step session');
            }

            this.isRunning = true;
            this.updateButtonStates();

        } catch (error) {
            console.error('Failed to step session:', error);
            this.showToast(error.message, 'error');
        }
    }

    async createSessionForStep() {
        const request = this.buildSessionRequest();
        request.runMode = 'Step';

        const response = await fetch('/api/session/create', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        });

        if (!response.ok) {
            throw new Error('Failed to create session');
        }

        const session = await response.json();
        this.currentSessionId = session.sessionId;
        document.getElementById('sessionId').textContent = session.sessionId.substring(0, 8) + '...';

        await this.connection.invoke('JoinSession', this.currentSessionId);
        this.clearTrace();
    }

    // Stop the current session
    async stopSession() {
        if (!this.currentSessionId) return;

        try {
            await fetch(`/api/session/${this.currentSessionId}/stop`, {
                method: 'POST'
            });

            this.isRunning = false;
            this.updateButtonStates();

        } catch (error) {
            console.error('Failed to stop session:', error);
            this.showToast('Failed to stop session', 'error');
        }
    }

    // Reset and create new session
    resetSession() {
        if (this.currentSessionId) {
            this.connection.invoke('LeaveSession', this.currentSessionId);
        }
        
        this.currentSessionId = null;
        this.messages = [];
        this.isRunning = false;
        this.updateButtonStates();
        this.clearTrace();
        this.updateStatus('Created');
        this.updateIterationCount(0);
        document.getElementById('sessionId').textContent = '-';
        document.getElementById('finalOutput').value = '';
        this.showToast('Session reset', 'info');
    }

    // Reset persona memory
    async resetPersonaMemory(persona) {
        if (!this.currentSessionId) {
            this.showToast('No active session', 'warning');
            return;
        }

        try {
            await fetch(`/api/session/${this.currentSessionId}/reset-memory/${persona}`, {
                method: 'POST'
            });
        } catch (error) {
            console.error('Failed to reset memory:', error);
            this.showToast('Failed to reset memory', 'error');
        }
    }

    // Build session request from form
    buildSessionRequest() {
        return {
            name: document.getElementById('sessionName').value,
            maxIterations: parseInt(document.getElementById('maxIterations').value),
            stopMarker: document.getElementById('stopMarker').value,
            stopOnReviewerApproved: document.getElementById('stopOnApproved').checked,
            runMode: document.getElementById('runMode').value,
            topic: document.getElementById('sessionTopic').value,
            
            creatorRootPrompt: document.getElementById('creatorPrompt').value,
            creatorModel: document.getElementById('creatorModel').value,
            creatorTemperature: parseFloat(document.getElementById('creatorTemp').value),
            creatorMaxTokens: parseInt(document.getElementById('creatorMaxTokens').value),
            creatorTopP: parseFloat(document.getElementById('creatorTopP').value),
            creatorPresencePenalty: parseFloat(document.getElementById('creatorPresencePenalty').value),
            creatorFrequencyPenalty: parseFloat(document.getElementById('creatorFrequencyPenalty').value),
            
            // Dynamic reviewers
            reviewers: this.getReviewerConfigs()
        };
    }

    // Update button states based on running state
    updateButtonStates() {
        document.getElementById('btnStart').disabled = this.isRunning;
        document.getElementById('btnStep').disabled = this.isRunning;
        document.getElementById('btnStop').disabled = !this.isRunning;
    }

    // Update status display
    updateStatus(status) {
        const statusEl = document.getElementById('sessionStatus');
        statusEl.textContent = status;
        statusEl.className = `status-value status-${status.toLowerCase()}`;
    }

    // Update iteration count display
    updateIterationCount(current) {
        const max = document.getElementById('maxIterations').value;
        document.getElementById('iterationCount').textContent = `${current} / ${max}`;
    }

    // Add iteration header to trace
    addIterationHeader(iteration) {
        const traceViewer = document.getElementById('traceViewer');
        
        // Remove empty state if present
        const emptyState = traceViewer.querySelector('.empty-state');
        if (emptyState) {
            emptyState.remove();
        }

        const header = document.createElement('div');
        header.className = 'iteration-header';
        header.textContent = `── Iteration ${iteration} ──`;
        traceViewer.appendChild(header);
        
        this.scrollToBottom();
    }

    // Get persona info (icon and color) - now handles dynamic reviewers
    getPersonaInfo(persona) {
        // Check if it's the Creator
        if (persona === 'Creator') {
            return { icon: '🎨', color: 'creator', name: 'Creator' };
        }
        
        // Check if it's a dynamic reviewer ID
        const reviewer = this.reviewers.find(r => r.id === persona);
        if (reviewer) {
            const index = this.reviewers.indexOf(reviewer);
            const icons = ['🔍', '📝', '✅', '🎯', '💡'];
            const colors = ['reviewer1', 'reviewer2', 'reviewer3', 'reviewer4', 'reviewer5'];
            return { 
                icon: icons[index % icons.length], 
                color: colors[index % colors.length],
                name: reviewer.name
            };
        }
        
        return { icon: '💬', color: 'system', name: persona };
    }

    // Start streaming a new message
    startStreamingMessage(messageId, persona, iteration) {
        const traceViewer = document.getElementById('traceViewer');
        const { icon, color, name } = this.getPersonaInfo(persona);
        
        const card = document.createElement('div');
        card.className = `message-card ${color}`;
        card.id = `msg-${messageId}`;
        
        card.innerHTML = `
            <div class="message-header">
                <div class="message-meta">
                    <span class="message-persona">${icon} ${name}</span>
                    <span class="message-timestamp">${new Date().toLocaleTimeString()}</span>
                    <span class="streaming-indicator">
                        <span class="streaming-dot"></span>
                        Streaming...
                    </span>
                </div>
                <div class="message-actions">
                    <button class="btn btn-small btn-secondary" onclick="dxoApp.copyMessageContent('${messageId}')">📋</button>
                    <button class="btn btn-small btn-secondary" onclick="dxoApp.toggleMessageExpand('${messageId}')">⬆</button>
                </div>
            </div>
            <div class="message-content" id="content-${messageId}"></div>
        `;
        
        traceViewer.appendChild(card);
        this.currentStreamingMessage = messageId;
        this.scrollToBottom();
    }

    // Append content to streaming message
    appendToStreamingMessage(messageId, content) {
        const contentEl = document.getElementById(`content-${messageId}`);
        if (contentEl) {
            contentEl.textContent += content;
            
            if (document.getElementById('autoScroll').checked) {
                this.scrollToBottom();
            }
        }
    }

    // Complete the streaming message
    completeStreamingMessage(messageId, fullContent) {
        const card = document.getElementById(`msg-${messageId}`);
        if (card) {
            // Remove streaming indicator
            const indicator = card.querySelector('.streaming-indicator');
            if (indicator) {
                indicator.remove();
            }
            
            // Update content with markdown rendering
            const contentEl = document.getElementById(`content-${messageId}`);
            if (contentEl) {
                // Store raw content as data attribute
                contentEl.dataset.rawContent = fullContent;
                
                // Try to render markdown
                try {
                    if (typeof marked !== 'undefined') {
                        contentEl.innerHTML = marked.parse(fullContent);
                    }
                } catch (e) {
                    // Keep as plain text if markdown fails
                    contentEl.textContent = fullContent;
                }
            }
        }

        // Save to messages array
        this.messages.push({
            messageId,
            content: fullContent,
            timestamp: new Date().toISOString()
        });

        this.currentStreamingMessage = null;
    }

    // Clear trace viewer
    clearTrace() {
        const traceViewer = document.getElementById('traceViewer');
        traceViewer.innerHTML = `
            <div class="empty-state">
                <p>No messages yet. Configure your personas and click Start to begin the orchestration loop.</p>
            </div>
        `;
        this.messages = [];
    }

    // Scroll trace to bottom
    scrollToBottom() {
        const traceViewer = document.getElementById('traceViewer');
        traceViewer.scrollTop = traceViewer.scrollHeight;
    }

    // Copy message content
    copyMessageContent(messageId) {
        const contentEl = document.getElementById(`content-${messageId}`);
        if (contentEl) {
            const text = contentEl.dataset.rawContent || contentEl.textContent;
            navigator.clipboard.writeText(text)
                .then(() => this.showToast('Copied to clipboard', 'success'))
                .catch(() => this.showToast('Failed to copy', 'error'));
        }
    }

    // Toggle message expand/collapse
    toggleMessageExpand(messageId) {
        const contentEl = document.getElementById(`content-${messageId}`);
        if (contentEl) {
            contentEl.classList.toggle('collapsed');
        }
    }

    // Copy final output
    copyOutput() {
        const output = document.getElementById('finalOutput').value;
        if (output) {
            navigator.clipboard.writeText(output)
                .then(() => this.showToast('Copied to clipboard', 'success'))
                .catch(() => this.showToast('Failed to copy', 'error'));
        } else {
            this.showToast('No output to copy', 'warning');
        }
    }

    // Download output
    downloadOutput(format) {
        const output = document.getElementById('finalOutput').value;
        if (!output) {
            this.showToast('No output to download', 'warning');
            return;
        }

        const filename = `dxo-output-${Date.now()}.${format}`;
        const blob = new Blob([output], { type: 'text/plain' });
        this.downloadBlob(blob, filename);
    }

    // Export transcript
    exportTranscript(format) {
        if (this.messages.length === 0) {
            this.showToast('No transcript to export', 'warning');
            this.closeExportModal();
            return;
        }

        let content, filename;

        if (format === 'json') {
            content = JSON.stringify({
                sessionId: this.currentSessionId,
                exportedAt: new Date().toISOString(),
                messages: this.messages
            }, null, 2);
            filename = `dxo-transcript-${Date.now()}.json`;
        } else {
            content = this.messages.map(m => {
                const card = document.getElementById(`msg-${m.messageId}`);
                const persona = card?.querySelector('.message-persona')?.textContent || 'Unknown';
                return `## ${persona}\n\n${m.content}\n\n---\n`;
            }).join('\n');
            filename = `dxo-transcript-${Date.now()}.md`;
        }

        const blob = new Blob([content], { type: 'text/plain' });
        this.downloadBlob(blob, filename);
        this.closeExportModal();
        this.showToast('Transcript exported', 'success');
    }

    // Download blob as file
    downloadBlob(blob, filename) {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    // Show export modal
    showExportModal() {
        document.getElementById('exportModal').classList.remove('hidden');
    }

    // Close export modal
    closeExportModal() {
        document.getElementById('exportModal').classList.add('hidden');
    }

    // Create toast container
    createToastContainer() {
        const container = document.createElement('div');
        container.className = 'toast-container';
        container.id = 'toastContainer';
        document.body.appendChild(container);
    }

    // Show toast notification
    showToast(message, type = 'info') {
        const container = document.getElementById('toastContainer');
        const toast = document.createElement('div');
        toast.className = `toast ${type}`;
        toast.textContent = message;
        container.appendChild(toast);

        setTimeout(() => {
            toast.remove();
        }, 4000);
    }

    // Open session settings flyout
    openSessionSettings() {
        const flyout = document.getElementById('sessionSettingsFlyout');
        flyout.classList.remove('hidden');
    }

    // Close session settings flyout
    closeSessionSettings() {
        const flyout = document.getElementById('sessionSettingsFlyout');
        flyout.classList.add('hidden');
    }

    // Open interaction stream lightbox
    openInteractionStream() {
        const lightbox = document.getElementById('interactionStreamLightbox');
        lightbox.classList.remove('hidden');
    }

    // Close interaction stream lightbox
    closeInteractionStream() {
        const lightbox = document.getElementById('interactionStreamLightbox');
        lightbox.classList.add('hidden');
    }

    // Open reviewer selector flyout
    async openReviewerSelector() {
        const flyout = document.getElementById('reviewerSelectionFlyout');
        flyout.classList.remove('hidden');
        
        // Load templates every time the flyout opens
        await this.loadReviewerTemplates();
    }

    // Close reviewer selector flyout
    closeReviewerSelector() {
        const flyout = document.getElementById('reviewerSelectionFlyout');
        flyout.classList.add('hidden');
    }

    // Load reviewer templates from JSON
    async loadReviewerTemplates() {
        const grid = document.getElementById('reviewerTemplatesGrid');
        grid.innerHTML = '<p style="color: var(--text-secondary); text-align: center;">Loading templates...</p>';
        
        try {
            const response = await fetch('/agentconfigurations.json');
            if (!response.ok) {
                throw new Error('Failed to load agent configurations');
            }
            
            const config = await response.json();
            let reviewerTemplates = config.agents?.reviewers || [];
            
            if (reviewerTemplates.length === 0) {
                grid.innerHTML = '<p style="color: var(--text-secondary); text-align: center;">No reviewer templates found.</p>';
                return;
            }
            
            // Sort templates alphabetically by role
            reviewerTemplates = reviewerTemplates.sort((a, b) => {
                return a.role.localeCompare(b.role);
            });
            
            // Get currently added reviewer roles to determine which templates are already used
            const addedRoles = this.reviewers.map(r => r.name);
            
            // Clear grid and populate with template cards
            grid.innerHTML = '';
            
            reviewerTemplates.forEach(template => {
                const isAdded = addedRoles.includes(template.role);
                const card = this.createTemplateCard(template, isAdded);
                grid.appendChild(card);
            });
            
        } catch (error) {
            console.error('Failed to load reviewer templates:', error);
            grid.innerHTML = '<p style="color: var(--danger-color); text-align: center;">Failed to load templates. Please try again.</p>';
            this.showToast('Failed to load reviewer templates', 'error');
        }
    }

    // Create a template card element
    createTemplateCard(template, isAdded = false) {
        const card = document.createElement('div');
        card.className = 'reviewer-template-card';
        card.dataset.templateRole = template.role;
        
        // Add disabled class if already added
        if (isAdded) {
            card.classList.add('disabled');
        }
        
        // Get category badge class
        const categoryClass = template.category.toLowerCase().replace('-', '-');
        
        // Extract first line of prompt as description (if available)
        const promptLines = template.prompt.split('\\n');
        const description = promptLines.length > 1 ? promptLines[1].trim() : 'Click to add this reviewer';
        
        card.innerHTML = `
            <div class="template-card-header">
                <h4 class="template-card-title">${template.role}</h4>
                <span class="template-category-badge ${categoryClass}">${template.category}</span>
            </div>
            <p class="template-card-description">${isAdded ? 'Already added' : description}</p>
        `;
        
        // Add click handler only if not already added
        if (!isAdded) {
            card.addEventListener('click', () => {
                this.selectReviewerTemplate(template);
            });
        }
        
        return card;
    }

    // Handle template selection
    selectReviewerTemplate(template) {
        // Format the prompt by replacing \n with actual line breaks
        const formattedPrompt = template.prompt.replace(/\\n/g, '\n');
        
        // Add reviewer with template data
        const reviewerNumber = this.reviewers.length + 1;
        this.addReviewer(template.role, formattedPrompt);
        
        // Close the flyout
        this.closeReviewerSelector();
        
        // Show success message
        this.showToast(`Added ${template.role}`, 'success');
    }

    // Check if a model is from xAI (Grok models)
    isXAIModel(modelName) {
        if (!modelName) return false;
        return modelName.toLowerCase().includes('grok');
    }

    // Handle creator model change to enable/disable penalty fields for xAI
    handleCreatorModelChange() {
        const modelSelect = document.getElementById('creatorModel');
        const isXAI = this.isXAIModel(modelSelect.value);
        
        const presencePenalty = document.getElementById('creatorPresencePenalty');
        const frequencyPenalty = document.getElementById('creatorFrequencyPenalty');
        
        if (isXAI) {
            presencePenalty.disabled = true;
            frequencyPenalty.disabled = true;
            presencePenalty.value = '0';
            frequencyPenalty.value = '0';
            presencePenalty.style.opacity = '0.5';
            frequencyPenalty.style.opacity = '0.5';
            presencePenalty.parentElement.title = 'xAI models do not support penalty parameters';
            frequencyPenalty.parentElement.title = 'xAI models do not support penalty parameters';
        } else {
            presencePenalty.disabled = false;
            frequencyPenalty.disabled = false;
            presencePenalty.style.opacity = '1';
            frequencyPenalty.style.opacity = '1';
            presencePenalty.parentElement.title = '';
            frequencyPenalty.parentElement.title = '';
        }
    }

    // Handle reviewer model change to enable/disable penalty fields for xAI
    handleReviewerModelChange(reviewerId) {
        const card = document.querySelector(`[data-reviewer-id="${reviewerId}"]`);
        if (!card) return;
        
        const modelSelect = card.querySelector('.reviewer-model');
        const isXAI = this.isXAIModel(modelSelect.value);
        
        const presencePenalty = card.querySelector('.reviewer-presence-penalty');
        const frequencyPenalty = card.querySelector('.reviewer-frequency-penalty');
        
        if (isXAI) {
            presencePenalty.disabled = true;
            frequencyPenalty.disabled = true;
            presencePenalty.value = '0';
            frequencyPenalty.value = '0';
            presencePenalty.style.opacity = '0.5';
            frequencyPenalty.style.opacity = '0.5';
            presencePenalty.parentElement.title = 'xAI models do not support penalty parameters';
            frequencyPenalty.parentElement.title = 'xAI models do not support penalty parameters';
        } else {
            presencePenalty.disabled = false;
            frequencyPenalty.disabled = false;
            presencePenalty.style.opacity = '1';
            frequencyPenalty.style.opacity = '1';
            presencePenalty.parentElement.title = '';
            frequencyPenalty.parentElement.title = '';
        }
    }
}

// Close export modal function (called from HTML)
function closeExportModal() {
    document.getElementById('exportModal').classList.add('hidden');
}

// Initialize app when DOM is ready
let dxoApp;
document.addEventListener('DOMContentLoaded', () => {
    dxoApp = new DXOApp();
});
