#!/usr/bin/env node
// A minimal MCP server (stdio, newline-delimited JSON-RPC) with one tool: it shows the user the QR
// code Veriqa returned for an approval. The hook has already saved that PNG next to the pending
// approval; the agent passes only the transaction id, so the image reaches the conversation as
// Veriqa drew it, without the agent copying it.
import { existsSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createInterface } from 'node:readline';

const ServerInfo = { name: 'veriqa-approval', version: '1.0.0' };
const DefaultProtocolVersion = '2025-06-18';
const ToolName = 'show_approval_qr';
const TransactionIdPattern = /^[A-Za-z0-9_-]{8,128}$/;

const JsonRpcError = { MethodNotFound: -32601, InvalidParams: -32602 };

const Tool = {
  name: ToolName,
  description: 'Shows the user the QR code of a pending Veriqa approval, so that they can scan it '
    + 'with their phone. Call it with the transaction id the approval request gave you.',
  inputSchema: {
    type: 'object',
    properties: {
      transaction_id: { type: 'string', description: 'The transaction id of the pending approval.' },
    },
    required: ['transaction_id'],
  },
};

function send(message) {
  process.stdout.write(`${JSON.stringify({ jsonrpc: '2.0', ...message })}\n`);
}

function toolError(text) {
  return { content: [{ type: 'text', text }], isError: true };
}

// The id names a file in the temp folder, so it is checked before it becomes part of a path.
function showQr(args) {
  const id = args?.transaction_id;
  if (typeof id !== 'string' || !TransactionIdPattern.test(id)) {
    return toolError('transaction_id is not a Veriqa transaction id.');
  }
  const file = join(tmpdir(), `veriqa-approval-${id}.png`);
  if (!existsSync(file)) {
    return toolError('No QR code is saved for this transaction: show the user the approval link instead.');
  }
  return {
    content: [
      { type: 'image', data: readFileSync(file).toString('base64'), mimeType: 'image/png' },
      { type: 'text', text: 'The QR code is shown to the user.' },
    ],
  };
}

function handle(request) {
  const { id, method, params } = request;
  if (id === undefined) return; // a notification: nothing to answer
  switch (method) {
    case 'initialize':
      send({ id, result: {
        protocolVersion: params?.protocolVersion ?? DefaultProtocolVersion,
        capabilities: { tools: {} },
        serverInfo: ServerInfo,
      } });
      return;
    case 'ping':
      send({ id, result: {} });
      return;
    case 'tools/list':
      send({ id, result: { tools: [Tool] } });
      return;
    case 'tools/call':
      if (params?.name !== ToolName) {
        send({ id, error: { code: JsonRpcError.InvalidParams, message: `Unknown tool: ${params?.name}` } });
        return;
      }
      send({ id, result: showQr(params.arguments) });
      return;
    default:
      send({ id, error: { code: JsonRpcError.MethodNotFound, message: `Method not found: ${method}` } });
  }
}

createInterface({ input: process.stdin }).on('line', (line) => {
  if (!line.trim()) return;
  try {
    handle(JSON.parse(line));
  } catch {
    // not JSON-RPC: ignored, the client times the request out
  }
});
