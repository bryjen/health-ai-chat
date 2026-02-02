// D3.js Force-Directed Graph for Symptom Correlation Map
// Converted from React component to vanilla JS for Blazor interop

const graphInstances = new Map();

function getContainer(elementId) {
    return document.getElementById(elementId);
}

function updateDimensions(elementId) {
    const container = getContainer(elementId);
    if (!container) return { width: 0, height: 0 };
    const rect = container.getBoundingClientRect();
    return { width: rect.width, height: rect.height };
}

export function initializeGraph(elementId, data) {
    const container = getContainer(elementId);
    if (!container || !data || !window.d3) {
        console.error('Graph initialization failed: container, data, or d3 not available');
        return;
    }

    // Parse JSON data if it's a string
    let graphData = data;
    if (typeof data === 'string') {
        try {
            graphData = JSON.parse(data);
        } catch (e) {
            console.error('Failed to parse graph data:', e);
            return;
        }
    }

    // Clean up existing instance if any
    if (graphInstances.has(elementId)) {
        disposeGraph(elementId);
    }

    const dimensions = updateDimensions(elementId);
    if (dimensions.width === 0 || dimensions.height === 0) {
        console.warn('Container has zero dimensions, retrying after delay');
        setTimeout(() => initializeGraph(elementId, data), 100);
        return;
    }

    const { width, height } = dimensions;
    const d3 = window.d3;

    // Create SVG
    const svg = d3.select(container)
        .append('svg')
        .attr('width', width)
        .attr('height', height)
        .style('cursor', 'move');

    // Create container group for zoom
    const g = svg.append('g');

    // Zoom behavior
    const zoom = d3.zoom()
        .scaleExtent([0.1, 4])
        .on('zoom', (event) => {
            g.attr('transform', event.transform);
        });

    svg.call(zoom);

    // Clone data to avoid mutation
    const nodes = graphData.nodes.map(d => ({ ...d }));
    const links = graphData.links.map(d => ({ ...d }));

    // Force simulation
    const simulation = d3.forceSimulation(nodes)
        .force('link', d3.forceLink(links).id(d => d.id).distance(100))
        .force('charge', d3.forceManyBody().strength(-300))
        .force('center', d3.forceCenter(width / 2, height / 2))
        .force('collide', d3.forceCollide().radius(d => (d.value * 2) + 20));

    // Add defs for filters and gradients
    const defs = svg.append('defs');
    
    // Glow filter for root nodes
    const filter = defs.append('filter')
        .attr('id', `glow-${elementId}`);
    filter.append('feGaussianBlur')
        .attr('stdDeviation', '3.5')
        .attr('result', 'coloredBlur');
    const feMerge = filter.append('feMerge');
    feMerge.append('feMergeNode').attr('in', 'coloredBlur');
    feMerge.append('feMergeNode').attr('in', 'SourceGraphic');
    
    // Radial gradient for cluster boundary
    const clusterGradient = defs.append('radialGradient')
        .attr('id', `cluster-gradient-${elementId}`)
        .attr('cx', '50%')
        .attr('cy', '50%')
        .attr('r', '50%');
    clusterGradient.append('stop')
        .attr('offset', '0%')
        .attr('stop-color', 'rgba(30, 80, 120, 0.3)');
    clusterGradient.append('stop')
        .attr('offset', '100%')
        .attr('stop-color', 'rgba(60, 150, 200, 0.1)');

    // Create cluster boundary group first (so it renders behind everything)
    const clusterGroup = g.append('g')
        .attr('class', 'cluster-boundary');
    
    // Draw Links
    const link = g.append('g')
        .attr('class', 'links')
        .selectAll('line')
        .data(links)
        .enter().append('line')
        .attr('stroke', '#06b6d4') // Cyan-500
        .attr('stroke-opacity', 0.4)
        .attr('stroke-width', d => Math.sqrt(d.value));

    // Draw Nodes
    const node = g.append('g')
        .attr('class', 'nodes')
        .selectAll('g')
        .data(nodes)
        .enter().append('g')
        .call(d3.drag()
            .on('start', dragstarted)
            .on('drag', dragged)
            .on('end', dragended));

    // Node Circles
    node.append('circle')
        .attr('r', d => {
            if (d.type === 'root') return 30;
            if (d.type === 'diagnosis') return 15;
            return 8;
        })
        .attr('fill', d => {
            if (d.type === 'root') return '#06b6d4';
            if (d.type === 'diagnosis') return '#a855f7';
            return '#0f172a';
        })
        .attr('stroke', '#06b6d4')
        .attr('stroke-width', d => d.type === 'root' ? 0 : 2)
        .attr('class', d => d.type === 'root' ? 'animate-pulse' : '');

    // Apply glow filter to root nodes
    node.filter(d => d.type === 'root')
        .style('filter', `url(#glow-${elementId})`);

    // Node Labels
    node.append('text')
        .text(d => d.label)
        .attr('x', d => d.type === 'root' ? 40 : 15)
        .attr('y', 5)
        .attr('fill', 'white')
        .attr('font-size', d => d.type === 'root' ? '16px' : '12px')
        .attr('font-family', 'monospace')
        .attr('opacity', 0.8)
        .style('pointer-events', 'none')
        .style('text-shadow', '0 0 5px #000');

    // Drag functions
    function dragstarted(event, d) {
        if (!event.active) simulation.alphaTarget(0.3).restart();
        d.fx = d.x;
        d.fy = d.y;
    }

    function dragged(event, d) {
        d.fx = event.x;
        d.fy = event.y;
    }

    function dragended(event, d) {
        if (!event.active) simulation.alphaTarget(0);
        d.fx = null;
        d.fy = null;
    }
    
    let clusterCircle = null;
    let clusterLabel = null;
    
    // Function to update cluster boundary based on node positions
    function updateClusterBoundary() {
        if (nodes.length === 0) return;
        
        // Calculate bounding box of all nodes
        let minX = Infinity, maxX = -Infinity;
        let minY = Infinity, maxY = -Infinity;
        
        nodes.forEach(d => {
            const radius = d.type === 'root' ? 30 : (d.type === 'diagnosis' ? 15 : 8);
            minX = Math.min(minX, d.x - radius);
            maxX = Math.max(maxX, d.x + radius);
            minY = Math.min(minY, d.y - radius);
            maxY = Math.max(maxY, d.y + radius);
        });
        
        // Calculate center and radius with padding
        const centerX = (minX + maxX) / 2;
        const centerY = (minY + maxY) / 2;
        const padding = 60; // Extra padding around nodes
        const radius = Math.max(
            Math.sqrt(Math.pow(maxX - minX, 2) + Math.pow(maxY - minY, 2)) / 2 + padding,
            150 // Minimum radius
        );
        
        // Remove existing cluster elements
        clusterGroup.selectAll('*').remove();
        
        // Draw cluster circle with gradient fill
        clusterCircle = clusterGroup.append('circle')
            .attr('cx', centerX)
            .attr('cy', centerY)
            .attr('r', radius)
            .attr('fill', `url(#cluster-gradient-${elementId})`)
            .attr('stroke', '#3a8fb7')
            .attr('stroke-width', 1)
            .attr('stroke-dasharray', '4,4')
            .style('pointer-events', 'none');
        
        // Add cluster label at the top
        clusterLabel = clusterGroup.append('text')
            .attr('x', centerX)
            .attr('y', centerY - radius - 10)
            .attr('text-anchor', 'middle')
            .attr('fill', '#3a8fb7')
            .attr('font-size', '10px')
            .attr('font-family', 'monospace')
            .attr('font-weight', 'bold')
            .attr('text-transform', 'uppercase')
            .attr('letter-spacing', '0.1em')
            .attr('opacity', 0.7)
            .style('pointer-events', 'none')
            .text('CLUSTER: INFLUENZA_A');
    }
    
    // Simulation tick handler
    simulation.on('tick', () => {
        link
            .attr('x1', d => d.source.x)
            .attr('y1', d => d.source.y)
            .attr('x2', d => d.target.x)
            .attr('y2', d => d.target.y);

        node
            .attr('transform', d => `translate(${d.x},${d.y})`);
        
        // Update cluster boundary on each tick
        updateClusterBoundary();
    });
    
    // Initial cluster boundary update (nodes may have initial positions)
    setTimeout(() => updateClusterBoundary(), 100);

    // Handle resize
    const resizeHandler = () => {
        const newDimensions = updateDimensions(elementId);
        if (newDimensions.width > 0 && newDimensions.height > 0) {
            svg.attr('width', newDimensions.width)
               .attr('height', newDimensions.height);
            simulation.force('center', d3.forceCenter(newDimensions.width / 2, newDimensions.height / 2));
            simulation.alpha(0.3).restart();
        }
    };

    window.addEventListener('resize', resizeHandler);

    // Store instance data
    graphInstances.set(elementId, {
        svg,
        simulation,
        resizeHandler,
        container
    });
}

export function updateGraph(elementId, data) {
    // Parse JSON data if it's a string
    let graphData = data;
    if (typeof data === 'string') {
        try {
            graphData = JSON.parse(data);
        } catch (e) {
            console.error('Failed to parse graph data:', e);
            return;
        }
    }
    
    // For now, just reinitialize with new data
    // Could be optimized to update nodes/links without full recreation
    disposeGraph(elementId);
    initializeGraph(elementId, graphData);
}

export function disposeGraph(elementId) {
    const instance = graphInstances.get(elementId);
    if (instance) {
        // Stop simulation
        if (instance.simulation) {
            instance.simulation.stop();
        }

        // Remove resize listener
        if (instance.resizeHandler) {
            window.removeEventListener('resize', instance.resizeHandler);
        }

        // Remove SVG
        if (instance.container && window.d3) {
            window.d3.select(instance.container).select('svg').remove();
        }

        graphInstances.delete(elementId);
    }
}
