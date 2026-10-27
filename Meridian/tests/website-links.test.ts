import { test } from 'node:test';
import assert from 'node:assert/strict';
import { websiteDestination } from '../src/website-links.ts';

test('website links open absolute HTTP and HTTPS destinations from editor and publications', () => {
  const website = 'https://learn.microsoft.com/en-us/dotnet/api/system.text?view=net-10.0#classes';
  for (const page of ['http://127.0.0.1:5240/?project=one&canvas=two', 'file:///C:/Works/Namespaces.meridian.html', 'blob:http://127.0.0.1:5240/one']) {
    assert.equal(websiteDestination(website, page), website);
    assert.equal(websiteDestination('http://example.com/path', page), 'http://example.com/path');
  }
});

test('application, local anchor, file, download and non-web scheme routing stays separate', () => {
  const page = 'http://127.0.0.1:5240/?project=one';
  for (const href of ['', ' ', '#section', '/?project=two&canvas=three', 'http://127.0.0.1:5240/?libraryCanvas=one', 'mailto:hello@example.com', 'file:///C:/file.pdf', 'javascript:alert(1)', 'data:text/html,test', 'blob:http://127.0.0.1:5240/one', 'https://user:password@example.com']) {
    assert.equal(websiteDestination(href, page), null, href);
  }
  assert.equal(websiteDestination('#section', 'file:///C:/Works/one.html'), null);
});

test('relative published links resolve safely without treating malformed addresses as websites', () => {
  assert.equal(websiteDestination('//example.com/one', 'https://atlasuniversalis.com/work'), 'https://example.com/one');
  assert.equal(websiteDestination('/one', 'https://atlasuniversalis.com/work'), null);
  assert.equal(websiteDestination('https://', 'https://atlasuniversalis.com/work'), null);
});
