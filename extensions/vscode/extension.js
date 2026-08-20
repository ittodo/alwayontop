'use strict';

const fs = require('node:fs');
const net = require('node:net');
const path = require('node:path');
const vscode = require('vscode');

const PIPE_PATH = '\\\\.\\pipe\\TrayAlwaysOnTop.VSCode';
const HEARTBEAT_MS = 4000;
const RECONNECT_MS = 1500;

const coreBindings = [
  ['ctrl+shift+p', 'workbench.action.showCommands', '명령 팔레트 열기'],
  ['f1', 'workbench.action.showCommands', '명령 팔레트 열기'],
  ['ctrl+p', 'workbench.action.quickOpen', '빠른 파일 열기'],
  ['ctrl+shift+n', 'workbench.action.newWindow', '새 창 열기'],
  ['ctrl+shift+w', 'workbench.action.closeWindow', '창 닫기'],
  ['ctrl+,', 'workbench.action.openSettings', '설정 열기'],
  ['ctrl+k ctrl+s', 'workbench.action.openGlobalKeybindings', '키보드 바로 가기 열기'],
  ['ctrl+n', 'workbench.action.files.newUntitledFile', '새 파일'],
  ['ctrl+o', 'workbench.action.files.openFile', '파일 열기'],
  ['ctrl+s', 'workbench.action.files.save', '저장', 'editorTextFocus'],
  ['ctrl+shift+s', 'workbench.action.files.saveAs', '다른 이름으로 저장', 'editorTextFocus'],
  ['ctrl+w', 'workbench.action.closeActiveEditor', '편집기 닫기'],
  ['ctrl+k f', 'workbench.action.closeFolder', '폴더 닫기'],
  ['ctrl+z', 'undo', '실행 취소', 'editorTextFocus'],
  ['ctrl+y', 'redo', '다시 실행', 'editorTextFocus'],
  ['ctrl+x', 'editor.action.clipboardCutAction', '잘라내기', 'editorTextFocus'],
  ['ctrl+c', 'editor.action.clipboardCopyAction', '복사', 'editorTextFocus'],
  ['ctrl+v', 'editor.action.clipboardPasteAction', '붙여넣기', 'editorTextFocus'],
  ['ctrl+a', 'editor.action.selectAll', '모두 선택', 'editorTextFocus'],
  ['ctrl+f', 'actions.find', '찾기', 'editorTextFocus'],
  ['ctrl+h', 'editor.action.startFindReplaceAction', '바꾸기', 'editorTextFocus'],
  ['f3', 'editor.action.nextMatchFindAction', '다음 찾기 결과', 'editorTextFocus'],
  ['shift+f3', 'editor.action.previousMatchFindAction', '이전 찾기 결과', 'editorTextFocus'],
  ['ctrl+g', 'workbench.action.gotoLine', '줄로 이동'],
  ['ctrl+shift+o', 'workbench.action.gotoSymbol', '파일 내 기호로 이동', 'editorTextFocus'],
  ['f12', 'editor.action.revealDefinition', '정의로 이동', 'editorTextFocus'],
  ['alt+f12', 'editor.action.peekDefinition', '정의 미리 보기', 'editorTextFocus'],
  ['shift+f12', 'editor.action.goToReferences', '참조로 이동', 'editorTextFocus'],
  ['f2', 'editor.action.rename', '기호 이름 바꾸기', 'editorTextFocus'],
  ['ctrl+space', 'editor.action.triggerSuggest', '제안 표시', 'editorTextFocus'],
  ['ctrl+.', 'editor.action.quickFix', '빠른 수정', 'editorTextFocus'],
  ['shift+alt+f', 'editor.action.formatDocument', '문서 서식 지정', 'editorTextFocus'],
  ['ctrl+/', 'editor.action.commentLine', '줄 주석 전환', 'editorTextFocus'],
  ['shift+alt+a', 'editor.action.blockComment', '블록 주석 전환', 'editorTextFocus'],
  ['alt+up', 'editor.action.moveLinesUpAction', '줄 위로 이동', 'editorTextFocus'],
  ['alt+down', 'editor.action.moveLinesDownAction', '줄 아래로 이동', 'editorTextFocus'],
  ['shift+alt+up', 'editor.action.copyLinesUpAction', '줄 위로 복사', 'editorTextFocus'],
  ['shift+alt+down', 'editor.action.copyLinesDownAction', '줄 아래로 복사', 'editorTextFocus'],
  ['ctrl+shift+k', 'editor.action.deleteLines', '줄 삭제', 'editorTextFocus'],
  ['ctrl+enter', 'editor.action.insertLineAfter', '아래에 줄 삽입', 'editorTextFocus'],
  ['ctrl+shift+enter', 'editor.action.insertLineBefore', '위에 줄 삽입', 'editorTextFocus'],
  ['ctrl+shift+\\', 'workbench.action.gotoBracket', '일치하는 괄호로 이동', 'editorTextFocus'],
  ['ctrl+shift+[', 'editor.fold', '영역 접기', 'editorTextFocus'],
  ['ctrl+shift+]', 'editor.unfold', '영역 펼치기', 'editorTextFocus'],
  ['f5', 'workbench.action.debug.start', '디버깅 시작'],
  ['ctrl+f5', 'workbench.action.debug.run', '디버깅 없이 실행'],
  ['f9', 'editor.debug.action.toggleBreakpoint', '중단점 전환', 'editorTextFocus'],
  ['f10', 'workbench.action.debug.stepOver', '프로시저 단위 실행', 'debugActive'],
  ['f11', 'workbench.action.debug.stepInto', '한 단계씩 코드 실행', 'debugActive'],
  ['shift+f11', 'workbench.action.debug.stepOut', '프로시저 나가기', 'debugActive'],
  ['shift+f5', 'workbench.action.debug.stop', '디버깅 중지', 'debugActive'],
  ['ctrl+shift+f', 'workbench.action.findInFiles', '파일에서 찾기'],
  ['ctrl+shift+h', 'workbench.action.replaceInFiles', '파일에서 바꾸기'],
  ['ctrl+shift+e', 'workbench.view.explorer', '탐색기 표시'],
  ['ctrl+shift+g', 'workbench.view.scm', '소스 제어 표시'],
  ['ctrl+shift+d', 'workbench.view.debug', '실행 및 디버그 표시'],
  ['ctrl+shift+x', 'workbench.view.extensions', '확장 표시'],
  ['ctrl+`', 'workbench.action.terminal.toggleTerminal', '터미널 전환'],
  ['ctrl+b', 'workbench.action.toggleSidebarVisibility', '사이드바 전환'],
  ['ctrl+j', 'workbench.action.togglePanel', '패널 전환'],
  ['ctrl+k z', 'workbench.action.toggleZenMode', '젠 모드 전환']
].map(([key, command, title, when]) => ({ key, command, title, when }));

let socket;
let reconnectTimer;
let heartbeatTimer;
let activated = false;

function activate(context) {
  activated = true;
  connect();
  const send = () => sendSnapshot();
  context.subscriptions.push(
    vscode.window.onDidChangeWindowState(send),
    vscode.window.onDidChangeActiveTextEditor(send),
    vscode.window.onDidChangeTextEditorSelection(send),
    vscode.window.onDidChangeActiveTerminal(send),
    vscode.debug.onDidStartDebugSession(send),
    vscode.debug.onDidTerminateDebugSession(send),
    vscode.workspace.onDidChangeConfiguration(send),
    vscode.extensions.onDidChange(send),
    { dispose: disconnect }
  );
  heartbeatTimer = setInterval(send, HEARTBEAT_MS);
  context.subscriptions.push({ dispose: () => clearInterval(heartbeatTimer) });
}

function deactivate() {
  activated = false;
  disconnect();
}

function connect() {
  if (!activated || socket) return;
  const candidate = net.createConnection(PIPE_PATH);
  candidate.setEncoding('utf8');
  candidate.once('connect', () => {
    socket = candidate;
    sendSnapshot();
  });
  candidate.once('error', () => {
    candidate.destroy();
    if (socket === candidate) socket = undefined;
    scheduleReconnect();
  });
  candidate.once('close', () => {
    if (socket === candidate) socket = undefined;
    scheduleReconnect();
  });
}

function disconnect() {
  clearTimeout(reconnectTimer);
  reconnectTimer = undefined;
  if (socket) socket.destroy();
  socket = undefined;
}

function scheduleReconnect() {
  if (!activated || reconnectTimer) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = undefined;
    connect();
  }, RECONNECT_MS);
}

function sendSnapshot() {
  if (!socket || socket.destroyed || !socket.writable) return;
  const editor = vscode.window.activeTextEditor;
  const context = buildContext(editor);
  const shortcuts = collectBindings(context);
  const message = {
    protocolVersion: 1,
    app: 'vscode',
    windowActive: vscode.window.state.focused,
    context: describeContext(context),
    languageId: editor?.document.languageId,
    shortcuts
  };
  socket.write(`${JSON.stringify(message)}\n`);
}

function buildContext(editor) {
  return {
    isWindows: process.platform === 'win32',
    editorFocus: Boolean(editor),
    editorTextFocus: Boolean(editor),
    textInputFocus: Boolean(editor),
    editorHasSelection: Boolean(editor && !editor.selection.isEmpty),
    editorHasMultipleSelections: Boolean(editor && editor.selections.length > 1),
    editorLangId: editor?.document.languageId,
    resourceLangId: editor?.document.languageId,
    resourceScheme: editor?.document.uri.scheme,
    isFileSystemResource: editor?.document.uri.scheme === 'file',
    debugActive: Boolean(vscode.debug.activeDebugSession),
    isWorkspaceTrusted: vscode.workspace.isTrusted
  };
}

function collectBindings(context) {
  const labels = new Map();
  const contributed = [];
  for (const extension of vscode.extensions.all) {
    const contribution = extension.packageJSON?.contributes;
    for (const command of contribution?.commands || []) {
      labels.set(command.command, localizeTitle(command.title) || command.command);
    }
    for (const binding of contribution?.keybindings || []) {
      const key = typeof binding === 'object' ? (binding.win || binding.key) : undefined;
      if (key && binding.command) {
        contributed.push({
          key,
          command: binding.command,
          title: labels.get(binding.command) || binding.command,
          when: binding.when
        });
      }
    }
  }

  const userBindings = readUserBindings().map(binding => ({
    key: binding.key,
    command: binding.command,
    title: labels.get(binding.command) || binding.command,
    when: binding.when
  }));
  const combined = [...coreBindings, ...contributed, ...userBindings];
  const removed = new Set(userBindings
    .filter(binding => binding.command?.startsWith('-'))
    .map(binding => `${binding.key}|${binding.command.slice(1)}`));

  const result = [];
  const seen = new Set();
  for (const binding of combined) {
    if (!binding.key || !binding.command || binding.command.startsWith('-')) continue;
    if (removed.has(`${binding.key}|${binding.command}`)) continue;
    if (!evaluateWhen(binding.when, context)) continue;
    const identity = `${binding.key}|${binding.command}`;
    if (seen.has(identity)) continue;
    seen.add(identity);
    result.push(binding);
  }
  return result.slice(0, 500);
}

function evaluateWhen(expression, context) {
  if (!expression || !expression.trim()) return true;
  const clauses = expression.split(/\s*&&\s*/);
  for (const clauseText of clauses) {
    const clause = clauseText.trim().replace(/^\((.*)\)$/, '$1').trim();
    if (!clause || clause.includes('||') || clause.includes('=~') || clause.includes(' in ')) return false;
    const comparison = clause.match(/^([\w.]+)\s*(==|!=|===|!==)\s*['"]?([^'"]+)['"]?$/);
    if (comparison) {
      const [, name, operator, expectedRaw] = comparison;
      if (!(name in context)) return false;
      const expected = expectedRaw.trim();
      const equal = String(context[name]) === expected;
      if ((operator === '==' || operator === '===') ? !equal : equal) return false;
      continue;
    }
    if (clause.startsWith('!')) {
      const name = clause.slice(1).trim();
      if (!(name in context) || Boolean(context[name])) return false;
      continue;
    }
    if (!(clause in context) || !Boolean(context[clause])) return false;
  }
  return true;
}

function readUserBindings() {
  try {
    const productFolder = vscode.env.appName.toLowerCase().includes('insiders') ? 'Code - Insiders' : 'Code';
    const file = path.join(process.env.APPDATA || '', productFolder, 'User', 'keybindings.json');
    if (!fs.existsSync(file)) return [];
    const parsed = JSON.parse(stripJsonComments(fs.readFileSync(file, 'utf8')));
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function stripJsonComments(text) {
  let output = '';
  let inString = false;
  let escaped = false;
  let lineComment = false;
  let blockComment = false;
  for (let index = 0; index < text.length; index++) {
    const current = text[index];
    const next = text[index + 1];
    if (lineComment) {
      if (current === '\n') { lineComment = false; output += current; }
      continue;
    }
    if (blockComment) {
      if (current === '*' && next === '/') { blockComment = false; index++; }
      continue;
    }
    if (!inString && current === '/' && next === '/') { lineComment = true; index++; continue; }
    if (!inString && current === '/' && next === '*') { blockComment = true; index++; continue; }
    output += current;
    if (inString && current === '\\' && !escaped) { escaped = true; continue; }
    if (current === '"' && !escaped) inString = !inString;
    escaped = false;
  }
  return output.replace(/,\s*([}\]])/g, '$1');
}

function localizeTitle(title) {
  if (typeof title === 'string') return title.startsWith('%') ? undefined : title;
  return title?.value;
}

function describeContext(context) {
  const parts = [];
  if (context.editorTextFocus) parts.push('편집기');
  if (context.editorLangId) parts.push(context.editorLangId);
  if (context.editorHasSelection) parts.push('선택 영역');
  if (context.debugActive) parts.push('디버깅');
  return parts.length ? parts.join(' · ') : '워크벤치';
}

module.exports = { activate, deactivate };
