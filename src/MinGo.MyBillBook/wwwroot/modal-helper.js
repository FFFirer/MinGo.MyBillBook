/**
 * Modal Helper - Manages body scroll lock when modals are open
 * Prevents background scrolling and ensures proper modal behavior on mobile
 */
(function() {
    'use strict';
    
    let modalCount = 0;
    let scrollPosition = 0;
    
    window.ModalHelper = {
        /**
         * Open modal - lock body scroll
         */
        open: function() {
            modalCount++;
            if (modalCount === 1) {
                // Save scroll position
                scrollPosition = window.pageYOffset || document.documentElement.scrollTop;
                
                // Add modal-open class to body
                document.body.classList.add('modal-open');
                document.body.style.top = `-${scrollPosition}px`;
            }
        },
        
        /**
         * Close modal - restore body scroll
         */
        close: function() {
            modalCount--;
            if (modalCount <= 0) {
                modalCount = 0;
                
                // Remove modal-open class from body
                document.body.classList.remove('modal-open');
                document.body.style.top = '';
                
                // Restore scroll position
                window.scrollTo(0, scrollPosition);
            }
        },
        
        /**
         * Force close all modals (emergency reset)
         */
        reset: function() {
            modalCount = 0;
            document.body.classList.remove('modal-open');
            document.body.style.top = '';
        }
    };
    
    // Auto-detect modal roots and attach observers
    const observer = new MutationObserver(function(mutations) {
        mutations.forEach(function(mutation) {
            if (mutation.type === 'childList') {
                mutation.addedNodes.forEach(function(node) {
                    if (node.classList && node.classList.contains('modal-root')) {
                        window.ModalHelper.open();
                    }
                });
                
                mutation.removedNodes.forEach(function(node) {
                    if (node.classList && node.classList.contains('modal-root')) {
                        window.ModalHelper.close();
                    }
                });
            }
        });
    });
    
    // Start observing when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() {
            observer.observe(document.body, { childList: true, subtree: true });
        });
    } else {
        observer.observe(document.body, { childList: true, subtree: true });
    }
})();
