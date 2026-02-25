// ===== K-win Landing Page Script =====

document.addEventListener('DOMContentLoaded', () => {
    initNavbar();
    initMobileMenu();
    initFAQ();
    initScreenshotTabs();
    initScrollReveal();
});

// ===== NAVBAR SCROLL EFFECT =====
function initNavbar() {
    const nav = document.querySelector('.nav');
    if (!nav) return;

    const onScroll = () => {
        nav.classList.toggle('scrolled', window.scrollY > 40);
    };

    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
}

// ===== MOBILE MENU TOGGLE =====
function initMobileMenu() {
    const toggle = document.querySelector('.nav-toggle');
    const navLinks = document.querySelector('.nav-links');
    if (!toggle || !navLinks) return;

    toggle.addEventListener('click', () => {
        navLinks.classList.toggle('open');
        const spans = toggle.querySelectorAll('span');
        if (navLinks.classList.contains('open')) {
            spans[0].style.transform = 'rotate(45deg) translate(5px, 5px)';
            spans[1].style.opacity = '0';
            spans[2].style.transform = 'rotate(-45deg) translate(5px, -5px)';
        } else {
            spans[0].style.transform = '';
            spans[1].style.opacity = '';
            spans[2].style.transform = '';
        }
    });

    // Close menu on link click
    navLinks.querySelectorAll('a').forEach(link => {
        link.addEventListener('click', () => {
            navLinks.classList.remove('open');
            const spans = toggle.querySelectorAll('span');
            spans.forEach(s => { s.style.transform = ''; s.style.opacity = ''; });
        });
    });
}

// ===== FAQ ACCORDION =====
function initFAQ() {
    const items = document.querySelectorAll('.faq-item');

    items.forEach(item => {
        const question = item.querySelector('.faq-question');
        if (!question) return;

        question.addEventListener('click', () => {
            const isOpen = item.classList.contains('open');

            // Close all
            items.forEach(i => i.classList.remove('open'));

            // Open clicked (if it wasn't already open)
            if (!isOpen) {
                item.classList.add('open');
            }
        });
    });
}

// ===== SCREENSHOT TABS =====
function initScreenshotTabs() {
    const tabs = document.querySelectorAll('.ss-tab');
    const contents = document.querySelectorAll('.placeholder-content');
    if (!tabs.length || !contents.length) return;

    tabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const target = tab.dataset.tab;

            // Update active tab
            tabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');

            // Show matching content
            contents.forEach(c => {
                if (c.id === target) {
                    c.classList.remove('hidden');
                } else {
                    c.classList.add('hidden');
                }
            });
        });
    });
}

// ===== SCROLL REVEAL ANIMATION =====
function initScrollReveal() {
    const revealElements = document.querySelectorAll(
        '.feature-card, .safety-card, .safety-banner, .download-card, .faq-item, .about-card, .why-card, .compat-card, .comparison-item'
    );

    if (!revealElements.length) return;

    // Use requestIdleCallback for non-critical animations
    const setupObserver = () => {
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.style.opacity = '1';
                    entry.target.style.transform = 'translateY(0)';
                    observer.unobserve(entry.target);
                }
            });
        }, {
            threshold: 0.1,
            rootMargin: '0px 0px -40px 0px'
        });

        revealElements.forEach((el, i) => {
            el.style.opacity = '0';
            el.style.transform = 'translateY(24px)';
            el.style.transition = `opacity 0.5s ease ${i % 3 * 0.1}s, transform 0.5s ease ${i % 3 * 0.1}s`;
            observer.observe(el);
        });
    };

    // Defer animation setup for better LCP
    if ('requestIdleCallback' in window) {
        requestIdleCallback(setupObserver);
    } else {
        setTimeout(setupObserver, 100);
    }
}

// ===== SMOOTH SCROLL FOR BETTER UX =====
function initSmoothScroll() {
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            const targetId = this.getAttribute('href');
            if (targetId === '#') return;
            
            const targetElement = document.querySelector(targetId);
            if (targetElement) {
                e.preventDefault();
                const navHeight = document.querySelector('.nav')?.offsetHeight || 80;
                const targetPosition = targetElement.getBoundingClientRect().top + window.pageYOffset - navHeight;
                
                window.scrollTo({
                    top: targetPosition,
                    behavior: 'smooth'
                });
                
                // Update URL without scroll
                history.pushState(null, null, targetId);
            }
        });
    });
}

// ===== LAZY LOAD IMAGES =====
function initLazyLoad() {
    const images = document.querySelectorAll('img[data-src]');
    if (!images.length) return;

    const imageObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                const img = entry.target;
                img.src = img.dataset.src;
                img.removeAttribute('data-src');
                imageObserver.unobserve(img);
            }
        });
    }, { rootMargin: '100px' });

    images.forEach(img => imageObserver.observe(img));
}

// ===== PERFORMANCE METRICS (for debugging) =====
function logPerformance() {
    if ('performance' in window && 'PerformanceObserver' in window) {
        // Log Largest Contentful Paint
        try {
            const observer = new PerformanceObserver((list) => {
                const entries = list.getEntries();
                const lastEntry = entries[entries.length - 1];
                console.log('[K-win] LCP:', Math.round(lastEntry.startTime), 'ms');
            });
            observer.observe({ type: 'largest-contentful-paint', buffered: true });
        } catch (e) {
            // LCP observer not supported
        }
    }
}

// Initialize smooth scroll on load
document.addEventListener('DOMContentLoaded', () => {
    initSmoothScroll();
    initLazyLoad();
    // Uncomment for debugging: logPerformance();
});

