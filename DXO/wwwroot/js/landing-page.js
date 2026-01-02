/* AI Decision Council - Landing Page Interactive Effects */

(function() {
    'use strict';

    // Wait for DOM to be ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initLandingPage);
    } else {
        initLandingPage();
    }

    function initLandingPage() {
        // Only run on landing page
        if (!document.querySelector('.landing-page-wrapper')) return;

        createParticles();
        initCursorEffects();
        init3DCardEffect();
    }

    // Create floating particles
    function createParticles() {
        const wrapper = document.querySelector('.landing-page-wrapper');
        if (!wrapper) return;

        const particleCount = 30;
        
        for (let i = 0; i < particleCount; i++) {
            const particle = document.createElement('div');
            particle.className = 'particle';
            
            // Random size between 2-8px
            const size = Math.random() * 6 + 2;
            particle.style.width = `${size}px`;
            particle.style.height = `${size}px`;
            
            // Random position
            particle.style.left = `${Math.random() * 100}%`;
            particle.style.top = `${Math.random() * 100}%`;
            
            // Random animation delay and duration
            particle.style.animationDelay = `${Math.random() * 20}s`;
            particle.style.animationDuration = `${15 + Math.random() * 10}s`;
            
            // Random opacity
            particle.style.opacity = Math.random() * 0.5 + 0.3;
            
            wrapper.appendChild(particle);
        }
    }

    // Cursor-following gradient orbs
    function initCursorEffects() {
        const wrapper = document.querySelector('.landing-page-wrapper');
        if (!wrapper) return;

        const orbs = document.querySelectorAll('.gradient-orb');
        let mouseX = 0;
        let mouseY = 0;
        let currentX = 0;
        let currentY = 0;

        // Track mouse position
        document.addEventListener('mousemove', (e) => {
            mouseX = e.clientX;
            mouseY = e.clientY;
        });

        // Smooth animation for orbs
        function animateOrbs() {
            // Smooth interpolation
            currentX += (mouseX - currentX) * 0.05;
            currentY += (mouseY - currentY) * 0.05;

            orbs.forEach((orb, index) => {
                const speed = 0.5 + (index * 0.2); // Different speeds for each orb
                const offsetX = (currentX - window.innerWidth / 2) * speed * 0.02;
                const offsetY = (currentY - window.innerHeight / 2) * speed * 0.02;
                
                orb.style.transform = `translate(${offsetX}px, ${offsetY}px)`;
            });

            requestAnimationFrame(animateOrbs);
        }

        animateOrbs();
    }

    // 3D title effect on mouse move (card tilt removed for better UX)
    function init3DCardEffect() {
        // 3D effect for title only
        const titleWrapper = document.querySelector('.landing-title-wrapper');
        if (titleWrapper) {
            document.addEventListener('mousemove', (e) => {
                const mouseX = e.clientX / window.innerWidth - 0.5;
                const mouseY = e.clientY / window.innerHeight - 0.5;
                
                const rotateX = mouseY * 10;
                const rotateY = mouseX * -10;
                
                titleWrapper.style.transform = `
                    translateY(-15px)
                    rotateX(${rotateX}deg) 
                    rotateY(${rotateY}deg)
                `;
            });
        }
    }


    // Performance optimization: Pause animations when tab is not visible
    let animationsPaused = false;

    document.addEventListener('visibilitychange', () => {
        if (document.hidden && !animationsPaused) {
            document.querySelectorAll('.gradient-orb, .particle').forEach(el => {
                el.style.animationPlayState = 'paused';
            });
            animationsPaused = true;
        } else if (!document.hidden && animationsPaused) {
            document.querySelectorAll('.gradient-orb, .particle').forEach(el => {
                el.style.animationPlayState = 'running';
            });
            animationsPaused = false;
        }
    });

})();