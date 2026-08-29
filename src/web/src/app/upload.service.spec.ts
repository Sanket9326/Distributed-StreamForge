import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UploadService } from './upload.service';

describe('UploadService', () => {
  let service: UploadService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(UploadService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('adds an inferred video content type for an MKV file', () => {
    service
      .upload(new File(['content'], 'source.mkv'), {
        title: 'Source title',
        description: 'Description',
        hashtags: ['dotnet', 'video'],
      })
      .subscribe();

    const request = http.expectOne('/api/uploads');
    const formData = request.request.body as FormData;
    expect(formData.get('title')).toBe('Source title');
    expect(formData.get('description')).toBe('Description');
    expect(formData.getAll('hashtags')).toEqual(['dotnet', 'video']);
    const submittedFile = formData.get('file') as File;
    expect(submittedFile.name).toBe('source.mkv');
    expect(submittedFile.type).toBe('video/x-matroska');
    expect(Array.from(formData.keys())).toEqual([
      'title',
      'description',
      'hashtags',
      'hashtags',
      'file',
    ]);
    request.flush({});
  });
});
