import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { App } from './app';
import { UploadReceipt } from './upload.service';

describe('App', () => {
  let fixture: ComponentFixture<App>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(App);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('renders the upload workspace', () => {
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Send your source');
    expect(fixture.nativeElement.querySelector('#video-title')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#video-description')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#video-hashtags')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('input[type=file]')).toBeTruthy();
  });

  it('rejects unsupported files before upload', () => {
    selectFile(new File(['not a video'], 'notes.txt', { type: 'text/plain' }));

    expect(fixture.nativeElement.textContent).toContain('Choose an MP4, MOV, WebM, or MKV video.');
    http.expectNone('/api/uploads');
  });

  it('reports progress and renders the upload receipt', () => {
    fillMetadata('Demo title', 'A useful demo', '#DotNet, video, dotnet');
    selectFile(new File(['video bytes'], 'demo.mp4', { type: 'video/mp4' }));
    clickButton('Upload video');

    const request = http.expectOne('/api/uploads');
    expect(request.request.method).toBe('POST');
    expect(request.request.reportProgress).toBe(true);
    const formData = request.request.body as FormData;
    expect(formData.get('title')).toBe('Demo title');
    expect(formData.get('description')).toBe('A useful demo');
    expect(formData.getAll('hashtags')).toEqual(['dotnet', 'video']);
    expect(formData.get('file')).toBeInstanceOf(File);

    request.event({ type: HttpEventType.UploadProgress, loaded: 5, total: 10 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('50%');

    const receipt: UploadReceipt = {
      id: 'e2c1bb104340452f9fc6a68cf4b12457',
      title: 'Demo title',
      description: 'A useful demo',
      hashtags: ['dotnet', 'video'],
      status: 'queued',
      fileName: 'demo.mp4',
      contentType: 'video/mp4',
      sizeBytes: 11,
      uploadedAtUtc: '2026-08-27T00:00:00Z',
      correlationId: 'test-correlation',
    };
    request.flush(receipt, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Video stored and queued');
    expect(fixture.nativeElement.textContent).toContain('Demo title');
    expect(fixture.nativeElement.textContent).toContain('test-correlation');
  });

  it('requires metadata and rejects invalid hashtags before upload', () => {
    selectFile(new File(['video bytes'], 'demo.mp4', { type: 'video/mp4' }));

    expect(fixture.nativeElement.textContent).toContain('Add a title before uploading.');
    expect((findButton('Upload video') as HTMLButtonElement).disabled).toBe(true);

    fillMetadata('Demo', '', 'valid, not valid');

    expect(fixture.nativeElement.textContent).toContain(
      'Hashtags may contain only letters, numbers, underscores, and hyphens.',
    );
    expect((findButton('Upload video') as HTMLButtonElement).disabled).toBe(true);
    http.expectNone('/api/uploads');
  });

  it('enforces title, description, hashtag count, and hashtag length limits', () => {
    selectFile(new File(['video bytes'], 'demo.mp4', { type: 'video/mp4' }));

    fillMetadata('t'.repeat(201), '', '');
    expect(fixture.nativeElement.textContent).toContain('The title cannot exceed 200 characters.');

    fillMetadata('Demo', 'd'.repeat(5_001), '');
    expect(fixture.nativeElement.textContent).toContain(
      'The description cannot exceed 5,000 characters.',
    );

    fillMetadata('Demo', '', 'one,two,three,four,five,six,seven,eight,nine,ten,eleven');
    expect(fixture.nativeElement.textContent).toContain('Add no more than 10 hashtags.');

    fillMetadata('Demo', '', 'h'.repeat(51));
    expect(fixture.nativeElement.textContent).toContain(
      'Each hashtag must be 50 characters or fewer.',
    );
    expect((findButton('Upload video') as HTMLButtonElement).disabled).toBe(true);
    http.expectNone('/api/uploads');
  });

  it('cancels an active upload', () => {
    fillMetadata('Demo title', '', 'webm');
    selectFile(new File(['video bytes'], 'demo.webm', { type: 'video/webm' }));
    clickButton('Upload video');

    const request = http.expectOne('/api/uploads');
    clickButton('Cancel upload');

    expect(request.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Upload cancelled');
    expect(inputValue('#video-title')).toBe('Demo title');
    expect(inputValue('#video-hashtags')).toBe('webm');
  });

  it('preserves metadata for retry and clears it on reset', () => {
    fillMetadata('Demo title', 'Description', '#DotNet, video');
    selectFile(new File(['video bytes'], 'demo.mp4', { type: 'video/mp4' }));
    clickButton('Upload video');

    const firstRequest = http.expectOne('/api/uploads');
    firstRequest.flush(
      { detail: 'Kafka is unavailable.', correlationId: 'retry-correlation' },
      { status: 503, statusText: 'Unavailable' },
    );
    fixture.detectChanges();

    expect(inputValue('#video-title')).toBe('Demo title');
    expect(inputValue('#video-description')).toBe('Description');
    expect(inputValue('#video-hashtags')).toBe('#DotNet, video');

    clickButton('Try again');
    const retryRequest = http.expectOne('/api/uploads');
    const retriedForm = retryRequest.request.body as FormData;
    expect(retriedForm.get('title')).toBe('Demo title');
    expect(retriedForm.getAll('hashtags')).toEqual(['dotnet', 'video']);
    retryRequest.flush(
      { detail: 'Still unavailable.' },
      { status: 503, statusText: 'Unavailable' },
    );
    fixture.detectChanges();

    clickButton('Choose another file');
    expect(inputValue('#video-title')).toBe('');
    expect(inputValue('#video-description')).toBe('');
    expect(inputValue('#video-hashtags')).toBe('');
    expect(fixture.nativeElement.querySelector('.file-card')).toBeNull();
  });

  function selectFile(file: File): void {
    const input = fixture.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    const files = {
      0: file,
      length: 1,
      item: (index: number) => (index === 0 ? file : null),
    } as unknown as FileList;
    Object.defineProperty(input, 'files', { configurable: true, value: files });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function clickButton(label: string): void {
    const button = findButton(label);
    expect(button).toBeTruthy();
    button!.click();
    fixture.detectChanges();
  }

  function findButton(label: string): HTMLButtonElement | undefined {
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    return buttons.find((candidate) => candidate.textContent?.includes(label));
  }

  function fillMetadata(title: string, description: string, hashtags: string): void {
    setInputValue('#video-title', title);
    setInputValue('#video-description', description);
    setInputValue('#video-hashtags', hashtags);
  }

  function setInputValue(selector: string, value: string): void {
    const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function inputValue(selector: string): string {
    return (fixture.nativeElement.querySelector(selector) as HTMLInputElement).value;
  }
});
